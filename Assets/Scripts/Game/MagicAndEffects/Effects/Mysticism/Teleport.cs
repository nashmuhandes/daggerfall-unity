﻿// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2018 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using FullSerializer;

namespace DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects
{
    /// <summary>
    /// Teleport
    /// </summary>
    public class Teleport : IncumbentEffect
    {
        #region Fields

        // Constants
        const int teleportOrSetAnchor = 4000;
        const int achorMustBeSet = 4001;

        // Effect data to serialize
        bool anchorSet = false;
        PlayerPositionData_v1 anchorPosition;
        int forcedRoundsRemaining = 1;

        // Volatile references
        SerializablePlayer serializablePlayer = null;
        PlayerEnterExit playerEnterExit = null;

        #endregion

        #region Overrides

        public override void SetProperties()
        {
            properties.Key = "Teleport-Effect";
            properties.ClassicKey = MakeClassicKey(43, 255);
            properties.GroupName = TextManager.Instance.GetText("ClassicEffects", "teleport");
            properties.SpellMakerDescription = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1602);
            properties.SpellBookDescription = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1302);
            properties.AllowedTargets = EntityEffectBroker.TargetFlags_Self;
            properties.AllowedElements = EntityEffectBroker.ElementFlags_MagicOnly;
            properties.AllowedCraftingStations = MagicCraftingStations.SpellMaker;
            properties.ShowSpellIcon = false;
            properties.MagicSkill = DFCareer.MagicSkills.Mysticism;
        }

        protected override int RemoveRound()
        {
            return forcedRoundsRemaining;
        }

        public override int RoundsRemaining
        {
            get { return forcedRoundsRemaining; }
        }

        protected override bool IsLikeKind(IncumbentEffect other)
        {
            return (other is Teleport);
        }

        public override void Start(EntityEffectManager manager, DaggerfallEntityBehaviour caster = null)
        {
            base.Start(manager, caster);
            CacheReferences();
        }

        public override void Resume(EntityEffectManager.EffectSaveData_v1 effectData, EntityEffectManager manager, DaggerfallEntityBehaviour caster = null)
        {
            base.Resume(effectData, manager, caster);
            CacheReferences();
        }

        protected override void BecomeIncumbent()
        {
            base.BecomeIncumbent();
            PromptPlayer();
        }

        protected override void AddState(IncumbentEffect incumbent)
        {
            // Prompt from incumbent as it has the position data for teleport
            (incumbent as Teleport).PromptPlayer();
        }

        public override void End()
        {
            base.End();
        }

        #endregion

        #region Private Methods

        void PromptPlayer()
        {
            // Get peered entity gameobject
            DaggerfallEntityBehaviour entityBehaviour = GetPeeredEntityBehaviour(manager);
            if (!entityBehaviour)
                return;

            // Target must be player - no effect on other entities
            if (entityBehaviour != GameManager.Instance.PlayerEntityBehaviour)
                return;

            // Prompt for outcome
            DaggerfallMessageBox mb = new DaggerfallMessageBox(DaggerfallUI.Instance.UserInterfaceManager, DaggerfallMessageBox.CommonMessageBoxButtons.AnchorTeleport, teleportOrSetAnchor, DaggerfallUI.Instance.UserInterfaceManager.TopWindow);
            mb.OnButtonClick += EffectActionPrompt_OnButtonClick;
            mb.Show();
        }

        void SetAnchor()
        {
            // Validate references
            if (!serializablePlayer || !playerEnterExit)
                return;

            // Get position information
            anchorPosition = serializablePlayer.GetPlayerPositionData();
            if (playerEnterExit.IsPlayerInsideBuilding)
            {
                anchorPosition.exteriorDoors = playerEnterExit.ExteriorDoors;
                anchorPosition.buildingDiscoveryData = playerEnterExit.BuildingDiscoveryData;
            }

            anchorSet = true;
        }

        void TeleportPlayer()
        {
            // Validate references
            if (!serializablePlayer || !playerEnterExit)
                return;

            // Is player in same interior as anchor?
            if (IsSameInterior())
            {
                // Just need to move player
                serializablePlayer.RestorePosition(anchorPosition);
                return;
            }
            else
            {
                // Need to load some other part of the world again - player could be anywhere
                // Esnure scene is cached for current location before departing
                SaveLoadManager.CacheScene(GameManager.Instance.StreamingWorld.SceneName);
                PlayerEnterExit.OnRespawnerComplete += PlayerEnterExit_OnRespawnerComplete;
                playerEnterExit.RestorePositionHelper(anchorPosition, false);
            }

            // End and resign
            // Player will need to create a new teleport with a new anchor from here
            // This is same behaviour as classic
            forcedRoundsRemaining = 0;
            ResignAsIncumbent();
            anchorSet = false;
        }

        #endregion

        #region Helpers

        // Cache required references in Start() or Resume() (only one or the other is called)
        void CacheReferences()
        {
            // Get peered SerializablePlayer and PlayerEnterExit
            serializablePlayer = caster.GetComponent<SerializablePlayer>();
            playerEnterExit = caster.GetComponent<PlayerEnterExit>();
            if (!serializablePlayer || !playerEnterExit)
            {
                Debug.LogError("Teleport effect could not find both SerializablePlayer and PlayerEnterExit components.");
                return;
            }
        }

        // Checks if player is in same building or dungeon interior as anchor
        bool IsSameInterior()
        {
            // Reject if outside
            if (!playerEnterExit.IsPlayerInside)
                return false;

            // Test depends on if player is inside a building or a dungeon
            if (playerEnterExit.IsPlayerInsideBuilding && anchorPosition.insideBuilding)
            {
                // Compare building key
                if (anchorPosition.buildingDiscoveryData.buildingKey == playerEnterExit.BuildingDiscoveryData.buildingKey)
                    return true;
            }
            else if (playerEnterExit.IsPlayerInsideDungeon && anchorPosition.insideDungeon)
            {
                // Compare map pixel of dungeon (only one dungeon per map pixel allowed)
                DaggerfallConnect.Utility.DFPosition anchorMapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(anchorPosition.worldPosX, anchorPosition.worldPosZ);
                DaggerfallConnect.Utility.DFPosition playerMapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
                if (anchorMapPixel.X == playerMapPixel.X && anchorMapPixel.Y == playerMapPixel.Y)
                    return true;
            }

            return false;
        }

        #endregion

        #region Event Handlers

        private void PlayerEnterExit_OnRespawnerComplete()
        {
            // Must have a caster and it must be the player
            if (caster == null || caster != GameManager.Instance.PlayerEntityBehaviour)
                return;

            // Get peered SerializablePlayer and PlayerEnterExit
            SerializablePlayer serializablePlayer = caster.GetComponent<SerializablePlayer>();
            PlayerEnterExit playerEnterExit = caster.GetComponent<PlayerEnterExit>();
            if (!serializablePlayer || !playerEnterExit)
            {
                Debug.LogError("Teleport effect OnRespawnerComplete() could not find both SerializablePlayer and PlayerEnterExit components.");
                return;
            }

            // Restore final position, unwire event, and restore cache
            serializablePlayer.RestorePosition(anchorPosition);
            PlayerEnterExit.OnRespawnerComplete -= PlayerEnterExit_OnRespawnerComplete;
            SaveLoadManager.RestoreCachedScene(GameManager.Instance.StreamingWorld.SceneName);
        }

        private void EffectActionPrompt_OnButtonClick(DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            sender.CloseWindow();

            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Anchor)
            {
                SetAnchor();
            }
            else if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Teleport)
            {
                if (!anchorSet)
                {
                    DaggerfallMessageBox mb = new DaggerfallMessageBox(DaggerfallUI.Instance.UserInterfaceManager, DaggerfallUI.Instance.UserInterfaceManager.TopWindow);
                    mb.SetTextTokens(achorMustBeSet);
                    mb.ClickAnywhereToClose = true;
                    mb.Show();
                    forcedRoundsRemaining = 0;
                    ResignAsIncumbent();
                    return;
                }
                TeleportPlayer();
            }
        }

        #endregion

        #region Serialization

        [fsObject("v1")]
        public struct SaveData_v1
        {
            public bool anchorSet;
            public PlayerPositionData_v1 anchorPosition;
            public int forcedRoundsRemaining;
        }

        public override object GetSaveData()
        {
            SaveData_v1 data = new SaveData_v1();
            data.anchorSet = anchorSet;
            data.anchorPosition = anchorPosition;
            data.forcedRoundsRemaining = forcedRoundsRemaining;

            return data;
        }

        public override void RestoreSaveData(object dataIn)
        {
            SaveData_v1 data = (SaveData_v1)dataIn;
            if (dataIn == null)
                return;

            anchorSet = data.anchorSet;
            anchorPosition = data.anchorPosition;
            forcedRoundsRemaining = data.forcedRoundsRemaining;
        }

        #endregion
    }
}