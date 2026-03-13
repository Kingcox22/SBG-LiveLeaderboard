using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using Mirror;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace SpectatorLeaderboard
{
    [BepInPlugin("com.kingcox22.sbg.liveleaderboard", "SBG-Live Leaderboard", "1.0.9")]
    public class SpectatorLeaderboardPlugin : BaseUnityPlugin
    {
        private ConfigEntry<float> _genUpdateInterval;
        private ConfigEntry<int> _genLeaderboardSize;
        private ConfigEntry<float> _specUpdateInterval;
        private ConfigEntry<int> _specLeaderboardSize;
        private ConfigEntry<float> _configX;
        private ConfigEntry<float> _configY;
        

        private float _timer = 0f;
        private GolfHole _activeHole;

        private class LeaderboardEntry 
        {
            public string Name;
            public float PlayerDist;
            public float BallDist;
            public bool Finished;
            public float VisualY; 
            public float TargetY; 
            public bool IsInitialized = false;
            // High value means not finished. Lower value = finished earlier.
            public float FinishOrder = float.MaxValue; 
        }

        private List<LeaderboardEntry> _leaderboardData = new List<LeaderboardEntry>();

        private void Awake()
        {
            // Define a range for the Update Interval (e.g., between 0.1s and 30s)
            var intervalRange = new AcceptableValueRange<float>(0.1f, 30f);
            
            // Define a range for the Leaderboard Size (e.g., between 1 and 50 players)
            var sizeRange = new AcceptableValueRange<int>(1, 32);

            _genUpdateInterval = Config.Bind("Player", "Update Interval", 1f, 
                new ConfigDescription("Seconds between updates.", intervalRange));
                
            _genLeaderboardSize = Config.Bind("Player", "Leaderboard Size", 16, 
                new ConfigDescription("Players to show.", sizeRange));

            _specUpdateInterval = Config.Bind("Spectator", "Update Interval", 5f, 
                new ConfigDescription("Seconds between updates when spectating.", intervalRange));
                
            _specLeaderboardSize = Config.Bind("Spectator", "Leaderboard Size", 16, 
                new ConfigDescription("Players to show when spectating.", sizeRange));

            var screenRangeX = new AcceptableValueRange<float>(0f, 1920f); // Adjust max based on typical res
            var screenRangeY = new AcceptableValueRange<float>(0f, 1080f);

            _configX = Config.Bind("Position", "X Position", 20f, 
                new ConfigDescription("Horizontal position of the leaderboard.", screenRangeX));
                
            _configY = Config.Bind("Position", "Y Position", 190f, 
                new ConfigDescription("Vertical position of the leaderboard.", screenRangeY));
        }

        private void Update()
        {
            if (!NetworkClient.active || GolfHoleManager.MainHole == null)
            {
                if (_leaderboardData.Count > 0) _leaderboardData.Clear();
                return;
            }

            float interval = (GameManager.LocalPlayerAsSpectator != null) ? _specUpdateInterval.Value : _genUpdateInterval.Value;
            _timer += Time.deltaTime;

            if (_timer >= interval)
            {
                _timer = 0f;
                RefreshLeaderboardData();
            }

            // Smooth Interpolation for the sliding effect
            foreach (var entry in _leaderboardData)
            {
                if (!entry.IsInitialized)
                {
                    entry.VisualY = entry.TargetY;
                    entry.IsInitialized = true;
                }
                entry.VisualY = Mathf.Lerp(entry.VisualY, entry.TargetY, Time.deltaTime * 8f);
            }
        }

        private void RefreshLeaderboardData()
        {
            if (_activeHole == null) _activeHole = GolfHoleManager.MainHole;
            if (_activeHole == null) return;

            Vector3 holePos = _activeHole.transform.position;
            bool isSpec = GameManager.LocalPlayerAsSpectator != null;
            int maxSize = isSpec ? _specLeaderboardSize.Value : _genLeaderboardSize.Value;

            try 
            {
                var currentGolfers = GameObject.FindObjectsByType<PlayerGolfer>(FindObjectsSortMode.None)
                    .Where(p => p != null && p.PlayerInfo != null && p.OwnBall != null)
                    .Select(p => {
                        float bDist = Vector3.Distance(p.OwnBall.transform.position, holePos);
                        bool gameFinished = false;
                        var fField = p.GetType().GetField("finishedHole", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fField != null) gameFinished = (bool)fField.GetValue(p);

                        return new { 
                            Name = GetPlayerName(p.PlayerInfo), 
                            PDist = Vector3.Distance(p.transform.position, holePos),
                            BDist = bDist,
                            Done = gameFinished || bDist <= 0.75f 
                        };
                    }).ToList();

                // 1. Sync data and lock finish order
                foreach (var data in currentGolfers)
                {
                    var entry = _leaderboardData.FirstOrDefault(x => x.Name == data.Name);
                    if (entry == null)
                    {
                        entry = new LeaderboardEntry { Name = data.Name };
                        _leaderboardData.Add(entry);
                    }

                    // If they just finished, record the time to lock their rank
                    if (data.Done && !entry.Finished)
                    {
                        entry.FinishOrder = Time.time;
                    }
                    else if (!data.Done)
                    {
                        entry.FinishOrder = float.MaxValue;
                    }

                    entry.PlayerDist = data.PDist;
                    entry.BallDist = data.BDist;
                    entry.Finished = data.Done;
                }

                // 2. Sort the persistent list
                _leaderboardData = _leaderboardData
                .OrderBy(x => x.FinishOrder)      // Locked spots first
                .ThenBy(x => x.PlayerDist)        // Use PlayerDist, not PDist
                .ThenBy(x => x.BallDist)          // Use BallDist, not BDist
                .Take(Mathf.Clamp(maxSize, 1, 50))
                .ToList();

                // 3. Assign TargetY for sliding
                float startY = _configY.Value + 35f + 10f; // yPos + header + padding
                for (int i = 0; i < _leaderboardData.Count; i++)
                {
                    _leaderboardData[i].TargetY = startY + (i * 25f);
                }

                _leaderboardData.RemoveAll(x => !currentGolfers.Any(g => g.Name == x.Name));
            }
            catch (Exception ex) { Logger.LogError(ex.Message); }
        }

        private void OnGUI()
        {
            if (!NetworkClient.active || _leaderboardData.Count == 0) return;

            float xPos = _configX.Value; // Use config value
            float yPos = _configY.Value; // Use config value 
            float width = 420f; 
            float rowHeight = 25f;
            float headerHeight = 35f;
            float padding = 10f;
            float boxHeight = headerHeight + (padding * 2) + (_leaderboardData.Count * rowHeight);

            GUI.backgroundColor = Color.black; 
            GUI.Box(new Rect(xPos, yPos, width, boxHeight), ""); 

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, richText = true };
            GUIStyle rightStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleRight };
            GUIStyle goldStyle = new GUIStyle(rightStyle) { normal = { textColor = new Color(1f, 0.85f, 0f) } };

            float headerY = yPos + padding;
            float firstRowY = headerY + headerHeight;

            // Draw Static Rank Numbers
            for (int i = 0; i < _leaderboardData.Count; i++)
            {
                GUI.Label(new Rect(xPos + padding, firstRowY + (i * rowHeight), 30, rowHeight), $"{i + 1}.", bodyStyle);
            }

            // Headers
            GUI.Label(new Rect(xPos + padding + 20, headerY, 140, headerHeight), "<b>PLAYER NAME</b>", bodyStyle);
            GUI.Label(new Rect(xPos + 180, headerY, 100, headerHeight), "<b>PLAYER DIST</b>", rightStyle);
            GUI.Label(new Rect(xPos + 290, headerY, 110, headerHeight), "<b>BALL DIST</b>", rightStyle);

            // Draw Sliding Names/Distances
            foreach (var entry in _leaderboardData)
            {
                if (!entry.IsInitialized) continue;

                string nameColor = entry.Finished ? "#00FF00" : "#FFFFFF";
                string statusText = entry.Finished ? "<b><color=#00FF00>FINISHED</color></b>" : $"{entry.BallDist:F1}m";

                GUI.Label(new Rect(xPos + padding + 20, entry.VisualY, 140, rowHeight), $"<color={nameColor}>{entry.Name}</color>", bodyStyle);
                GUI.Label(new Rect(xPos + 180, entry.VisualY, 100, rowHeight), $"{entry.PlayerDist:F1}m", goldStyle);
                GUI.Label(new Rect(xPos + 290, entry.VisualY, 110, rowHeight), statusText, rightStyle);
            }
        }

        private string GetPlayerName(PlayerInfo info)
        {
            if (info == null) return "Unknown";
            var type = info.GetType();
            var field = type.GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(info)?.ToString() ?? "Unknown";

            Component playerId = info.GetComponent("PlayerId");
            if (playerId != null)
            {
                var f = playerId.GetType().GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(playerId)?.ToString() ?? "Unknown";
            }
            return "Golfer"; 
        }
    }
}