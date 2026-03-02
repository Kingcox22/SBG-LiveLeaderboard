using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Mirror;
using System.Linq;
using System.Collections.Generic;

namespace SpectatorLeaderboard
{
    [BepInPlugin("com.dan.sbg.leaderboard", "Spectator Leaderboard", "1.0")]
    public class SpectatorLeaderboardPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> _configSpecInterval;
        private static ConfigEntry<int> _configLeaderboardCount;

        private float _specTimer = 0f;
        private Transform _cachedHole;
        private List<string> _leaderboardStrings = new List<string>();
        private string _spectatingDist = "0m";
        private bool _isCustomSpectatorActive = false;

        private void Awake()
        {
            _configSpecInterval = Config.Bind("Spectator", "Update Interval", 5f, "Seconds between leaderboard updates."); 
            _configLeaderboardCount = Config.Bind("Spectator", "Leaderboard Size", 5, "Number of players to show on the leaderboard.");

            PlayerSpectator.LocalPlayerIsSpectatingChanged += OnGameSpectateChanged; 
            PlayerSpectator.LocalPlayerStoppedSpectating += OnGameSpectateStopped; 

            var harmony = new Harmony("com.dan.sbg.leaderboard");
            harmony.PatchAll();
        }

        private void Update()
        {
            _specTimer += Time.deltaTime;
            if (_specTimer >= _configSpecInterval.Value)
            {
                _specTimer = 0f;
                RefreshLeaderboardLogic();
            }
        }

        private void OnGameSpectateChanged()  
        {
            var localSpec = GameObject.FindObjectsByType<PlayerSpectator>(FindObjectsSortMode.None)
                .FirstOrDefault(s => s.isLocalPlayer);
            
            if (localSpec != null)
            {
                _isCustomSpectatorActive = localSpec.IsSpectating;
            }
        }

        private void OnGameSpectateStopped()
        {
            _isCustomSpectatorActive = false;
            _cachedHole = null;
        }

        private void RefreshLeaderboardLogic()
        {
            if (!_isCustomSpectatorActive) return;

            if (_cachedHole == null)
            {
                // Updated to include the Desert Main Hole variant
                GameObject hole = GameObject.Find("Hole") ?? 
                                 GameObject.Find("Main hole") ?? 
                                 GameObject.Find("Desert Main Hole");
                
                if (hole != null) _cachedHole = hole.transform;
            }

            if (_cachedHole == null) return;

            var golfers = GameObject.FindObjectsByType<PlayerGolfer>(FindObjectsSortMode.None)
                .Where(g => g != null && !CourseManager.IsPlayerSpectator(g))
                .ToList();

            if (golfers.Count == 0) return;

            var sortedGolfers = golfers
                .Select(g => new { Golfer = g, Distance = Vector3.Distance(g.transform.position, _cachedHole.position) })
                .OrderBy(x => x.Distance).ToList();

            _leaderboardStrings.Clear();
            int displayCount = Mathf.Min(_configLeaderboardCount.Value, sortedGolfers.Count);
            
            for (int i = 0; i < displayCount; i++)
            {
                _leaderboardStrings.Add($"{i + 1}. {sortedGolfers[i].Golfer.name} ({Mathf.Round(sortedGolfers[i].Distance)}m)");
            }

            _spectatingDist = $"{Mathf.Round(sortedGolfers[0].Distance)}m";
        }

        private void OnGUI()
        {
            if (!_isCustomSpectatorActive) return;

            // Shifted down by 115px + original 15px margin = 130px
            float xPos = 15f;
            float yPos = 130f; 
            float width = 250f;
            float height = 65f + (_leaderboardStrings.Count * 22f);

            GUI.backgroundColor = new Color(0, 0, 0, 0.9f);
            Rect guiRect = new Rect(xPos, yPos, width, height);
            GUI.Box(guiRect, $"<b><size=14><color=white>[HOLE TRACKER]</color></size></b>");

            GUIStyle textStyle = new GUIStyle(GUI.skin.label) { richText = true };
            
            GUI.Label(new Rect(xPos + 10, yPos + 35, 200, 25), $"<b>Closest:</b> <color=yellow>{_spectatingDist}</color>", textStyle);
            
            for (int i = 0; i < _leaderboardStrings.Count; i++)
            {
                GUI.Label(new Rect(xPos + 15, yPos + 65 + (i * 22), 220, 25), _leaderboardStrings[i], textStyle);
            }
        }
    }
}