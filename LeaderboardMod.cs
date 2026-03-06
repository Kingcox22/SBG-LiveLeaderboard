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
    [BepInPlugin("com.kingcox22.sbg.liveleaderboard", "SBG-Live Leaderboard", "1.0.2")]
    public class SpectatorLeaderboardPlugin : BaseUnityPlugin
    {
        private ConfigEntry<float> _genUpdateInterval;
        private ConfigEntry<int> _genLeaderboardSize;
        private ConfigEntry<float> _specUpdateInterval;
        private ConfigEntry<int> _specLeaderboardSize;

        private float _timer = 0f;
        private GolfHole _activeHole;
        
        private struct LeaderboardEntry
        {
            public string Name;
            public float PlayerDist;
            public float BallDist;
        }

        private List<LeaderboardEntry> _leaderboardData = new List<LeaderboardEntry>();

        private void Awake()
        {
            _genUpdateInterval = Config.Bind("General", "Update Interval", 1f, "Seconds between updates.");
            _genLeaderboardSize = Config.Bind("General", "Leaderboard Size", 16, "Players to show.");
            _specUpdateInterval = Config.Bind("Spectator", "Update Interval", 5f, "Seconds between updates when spectating.");
            _specLeaderboardSize = Config.Bind("Spectator", "Leaderboard Size", 16, "Players to show when spectating.");

            Logger.LogInfo("SBG-Live Leaderboard v1.0.2 loaded with MissingFieldException fix.");
        }

        private void Update()
        {
            if (!NetworkClient.active || GolfHoleManager.MainHole == null)
            {
                if (_leaderboardData.Count > 0) _leaderboardData.Clear();
                return;
            }

            bool isSpec = GameManager.LocalPlayerAsSpectator != null;
            float interval = isSpec ? _specUpdateInterval.Value : _genUpdateInterval.Value;

            _timer += Time.deltaTime;
            if (_timer >= interval)
            {
                _timer = 0f;
                RefreshLeaderboardData(isSpec);
            }
        }

        private string GetPlayerName(PlayerInfo info)
        {
            if (info == null) return "Unknown";

            // 1. Try Live Build: Look for 'playerName' on PlayerInfo
            Type infoType = info.GetType();
            var liveField = infoType.GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (liveField != null) return liveField.GetValue(info)?.ToString() ?? "Unknown";

            // 2. Try Early Access: Look for 'PlayerId' component
            // We use the string "PlayerId" so we don't need the EA DLLs to compile.
            Component playerIdComponent = info.GetComponent("PlayerId");
            if (playerIdComponent != null)
            {
                Type idType = playerIdComponent.GetType();
                // The most likely candidates in EA/Mirror builds
                string[] namesToTry = { "_playerName", "networkedPlayerName", "playerName", "displayName", "Name" };

                foreach (string name in namesToTry)
                {
                    // Check for a FIELD (variable)
                    FieldInfo f = idType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) return f.GetValue(playerIdComponent)?.ToString() ?? "Unknown";

                    // Check for a PROPERTY (getter/setter) - This is likely what we missed!
                    PropertyInfo p = idType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null) return p.GetValue(playerIdComponent)?.ToString() ?? "Unknown";
                }
            }

            return "Golfer"; 
        }

        private void RefreshLeaderboardData(bool isSpectating)
        {
            if (_activeHole == null) _activeHole = GolfHoleManager.MainHole;
            if (_activeHole == null) return;

            Vector3 holePos = _activeHole.transform.position;
            int maxSize = isSpectating ? _specLeaderboardSize.Value : _genLeaderboardSize.Value;

            try 
            {
                var players = GameObject.FindObjectsByType<PlayerGolfer>(FindObjectsSortMode.None)
                    .Where(p => p != null && p.PlayerInfo != null && p.OwnBall != null)
                    .Select(p => new LeaderboardEntry
                    {
                        // CHANGE THIS LINE: Use the helper method, not the direct field
                        Name = GetPlayerName(p.PlayerInfo), 
                        PlayerDist = Vector3.Distance(p.transform.position, holePos),
                        BallDist = Vector3.Distance(p.OwnBall.transform.position, holePos)
                    })
                    .OrderBy(x => x.PlayerDist)
                    .Take(Mathf.Clamp(maxSize, 1, 50))
                    .ToList();

                _leaderboardData = players;
            }
            catch (Exception ex)
            {
                // Using BepInEx Logger
                Logger.LogError($"Leaderboard Refresh Failed: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            if (!NetworkClient.active || _leaderboardData.Count == 0) return;

            // Layout settings
            float xPos = 20f;
            float yPos = 190f; 
            float width = 420f; 
            float rowHeight = 25f;
            float headerHeight = 35f;
            float padding = 10f;
            float nameWidth = 160f;
            float distWidth = 120f;

            float boxHeight = headerHeight + padding + (_leaderboardData.Count * rowHeight) + padding;

            GUI.backgroundColor = new Color(0, 0, 0, 0.9f); 
            GUI.Box(new Rect(xPos, yPos, width, boxHeight), ""); 

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            headerStyle.normal.textColor = Color.white;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
            bodyStyle.normal.textColor = Color.white;

            GUIStyle distStyle = new GUIStyle(bodyStyle);
            distStyle.normal.textColor = new Color(1f, 0.85f, 0f); 

            float currentY = yPos + padding;

            GUI.Label(new Rect(xPos + padding, currentY, nameWidth, headerHeight), "<b>Player Name</b>", headerStyle);
            GUI.Label(new Rect(xPos + padding + nameWidth, currentY, distWidth, headerHeight), "<b>Player Distance</b>", headerStyle);
            GUI.Label(new Rect(xPos + padding + nameWidth + distWidth, currentY, distWidth, headerHeight), "<b>Ball Distance</b>", headerStyle);

            currentY += headerHeight;

            GUI.color = new Color(1, 1, 1, 0.5f);
            GUI.Box(new Rect(xPos + padding, currentY - 5, width - (padding * 2), 2), "");
            GUI.color = Color.white;

            for (int i = 0; i < _leaderboardData.Count; i++)
            {
                LeaderboardEntry entry = _leaderboardData[i];
                float rowY = currentY + (i * rowHeight);

                GUI.Label(new Rect(xPos + padding, rowY, nameWidth, rowHeight), $"{i+1}. {entry.Name}", bodyStyle);
                GUI.Label(new Rect(xPos + padding + nameWidth, rowY, distWidth, rowHeight), $"{entry.PlayerDist:F1}m", distStyle);
                GUI.Label(new Rect(xPos + padding + nameWidth + distWidth, rowY, distWidth, rowHeight), $"{entry.BallDist:F1}m", distStyle);
            }
        }
    }
}