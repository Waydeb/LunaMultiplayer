﻿using LunaClient.Base;
using LunaClient.Events;
using LunaClient.Localization;
using LunaClient.Systems.Lock;
using LunaClient.Systems.SafetyBubble;
using LunaClient.Systems.SettingsSys;
using LunaClient.VesselUtilities;
using System;
using System.Collections.Generic;
using UniLinq;

namespace LunaClient.Systems.VesselLockSys
{
    /// <summary>
    /// This class handles the locks in the vessel
    /// </summary>
    public class VesselLockSystem : System<VesselLockSystem>
    {
        #region Fields & properties

        public const string SpectateLock = "LMP_Spectating";
        public const ControlTypes BlockAllControls = ControlTypes.ALLBUTCAMERAS ^ ControlTypes.MAP ^ ControlTypes.PAUSE ^
                                                     ControlTypes.APPLAUNCHER_BUTTONS ^ ControlTypes.VESSEL_SWITCHING ^ ControlTypes.GUI;

        private ScreenMessage _spectateMessage;

        private string GetVesselOwner => VesselCommon.IsSpectating ?
            LockSystem.LockQuery.GetControlLockOwner(FlightGlobals.ActiveVessel.id) :
            "";

        private VesselLockEvents VesselLockEvents { get; } = new VesselLockEvents();
        
        private string SpectatingMessage => VesselCommon.IsSpectating ? LocalizationContainer.ScreenText.Spectating + $" {GetVesselOwner}." : "";

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(VesselLockSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            GameEvents.onVesselChange.Add(VesselLockEvents.OnVesselChange);
            GameEvents.onGameSceneLoadRequested.Add(VesselLockEvents.OnSceneRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(VesselLockEvents.LevelLoaded);
            GameEvents.onVesselLoaded.Add(VesselLockEvents.VesselLoaded);
            LockEvent.onLockAcquireUnityThread.Add(VesselLockEvents.LockAcquire);
            LockEvent.onLockReleaseUnityThread.Add(VesselLockEvents.LockReleased);
            VesselUnloadEvent.onVesselUnloading.Add(VesselLockEvents.VesselUnloading);

            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, UpdateOnScreenSpectateMessage));
            SetupRoutine(new RoutineDefinition(10000, RoutineExecution.Update, LockSystem.Singleton.MessageSender.SendLocksRequest));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            GameEvents.onVesselChange.Remove(VesselLockEvents.OnVesselChange);
            GameEvents.onGameSceneLoadRequested.Remove(VesselLockEvents.OnSceneRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(VesselLockEvents.LevelLoaded);
            GameEvents.onVesselLoaded.Remove(VesselLockEvents.VesselLoaded);
            LockEvent.onLockAcquireUnityThread.Remove(VesselLockEvents.LockAcquire);
            LockEvent.onLockReleaseUnityThread.Remove(VesselLockEvents.LockReleased);
            VesselUnloadEvent.onVesselUnloading.Remove(VesselLockEvents.VesselUnloading);
        }

        #endregion

        #region Update methods
        
        /// <summary>
        /// Show a message on the screen if we are spectating
        /// </summary>
        private void UpdateOnScreenSpectateMessage()
        {
            if (VesselCommon.IsSpectating)
            {
                if (_spectateMessage != null)
                    _spectateMessage.duration = 0f;
                _spectateMessage = LunaScreenMsg.PostScreenMessage(SpectatingMessage, 1000 * 2, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                if (_spectateMessage != null)
                {
                    _spectateMessage.duration = 0f;
                    _spectateMessage = null;
                }
            }
        }

        #endregion

        #region Public methods

        public void StartSpectating(Guid spectatingVesselId)
        {
            //Lock all vessel controls
            InputLockManager.SetControlLock(BlockAllControls, SpectateLock);

            var currentSpectatorLock = LockSystem.LockQuery.GetSpectatorLock(SettingsSystem.CurrentSettings.PlayerName);
            if (FlightGlobals.ActiveVessel != null && currentSpectatorLock == null)
                LockSystem.Singleton.AcquireSpectatorLock(FlightGlobals.ActiveVessel.id);

            VesselCommon.IsSpectating = true;
            VesselCommon.SpectatingVesselId = spectatingVesselId;

            //Disable "EVA" button
            HighLogic.CurrentGame.Parameters.Flight.CanEVA = false;
            SpectateEvent.onStartSpectating.Fire();
        }

        public void StopSpectating()
        {
            InputLockManager.RemoveControlLock(SpectateLock);
            LockSystem.Singleton.ReleaseSpectatorLock();
            VesselCommon.IsSpectating = false;

            if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.Parameters != null && HighLogic.CurrentGame.Parameters.Flight != null)
                HighLogic.CurrentGame.Parameters.Flight.CanEVA = true;
            SpectateEvent.onFinishedSpectating.Fire();
        }

        #endregion

        #region Private methods


        /// <summary>
        /// Tries to get the control lock for a vessel in case it's possible to do so
        /// </summary>
        private static void TryGetControlLockForVessel(Vessel vessel)
        {
            if (vessel == null) return;

            if (!LockSystem.LockQuery.ControlLockExists(vessel.id))
            {
                LockSystem.Singleton.AcquireControlLock(vessel.id);
                LockSystem.Singleton.AcquireKerbalLock(vessel);
            }
        }

        /// <summary>
        /// Tries/force getting the update and unloaded update locks for a vessel.
        /// </summary>
        private static void GetUpdateLocksForVessel(Vessel vessel, bool force)
        {
            if (vessel == null) return;

            LockSystem.Singleton.AcquireUpdateLock(vessel.id, force);
            LockSystem.Singleton.AcquireUnloadedUpdateLock(vessel.id, force);
        }

        /// <summary>
        /// Return the vessel ids of the vessels where we have an update lock
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<Guid> GetVesselIdsWeCurrentlyUpdate()
        {
            //An spectator should never have Update/UnloadedUpdate locks
            if (VesselCommon.IsSpectating)
                return new Guid[0];

            return LockSystem.LockQuery
                .GetAllUpdateLocks(SettingsSystem.CurrentSettings.PlayerName)
                .Select(l => l.VesselId);
        }

        /// <summary>
        /// Return the OTHER vessel ids of the vessels that are unloadedloaded not dead, not in safety bubble 
        /// and that nobody has the unloaded update or update lock
        /// </summary>
        private static IEnumerable<Guid> GetValidUnloadedVesselIds()
        {
            return FlightGlobals.Vessels
                .Where(v => v != null && v.state != Vessel.State.DEAD && !v.loaded &&
                            v.id != FlightGlobals.ActiveVessel?.id &&
                            !SafetyBubbleSystem.Singleton.IsInSafetyBubble(v) &&
                            !v.LandedOrSplashed && //DO NOT get unloaded locks on landed vessels!
                            !LockSystem.LockQuery.UnloadedUpdateLockExists(v.id) &&
                            !LockSystem.LockQuery.UpdateLockExists(v.id))
                .Select(v => v.id);
        }

        /// <summary>
        /// Return the OTHER vessel ids of the vessels that are loaded (close to us) not dead and not in safety bubble.
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<Guid> GetValidSecondaryVesselIds()
        {
            //An spectator should never have Update/UnloadedUpdate locks
            if (VesselCommon.IsSpectating)
                return new Guid[0];

            return FlightGlobals.VesselsLoaded
                .Where(v => v != null && v.state != Vessel.State.DEAD &&
                            v.id != FlightGlobals.ActiveVessel?.id &&
                            !SafetyBubbleSystem.Singleton.IsInSafetyBubble(v) &&
                            !LockSystem.LockQuery.UpdateLockExists(v.id))
                .Select(v => v.id);
        }

        /// <summary>
        /// Return the vessel ids of the OTHER vessels that are far, dead, in safety bubble, and being updated by us.
        /// We use this list to relase the locks as we shouldn't update them
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<Guid> GetSecondaryVesselIdsThatShouldBeReleased()
        {
            //An spectator should never have Update/UnloadedUpdate locks
            if (VesselCommon.IsSpectating)
            {
                return LockSystem.LockQuery.GetAllUpdateLocks(SettingsSystem.CurrentSettings.PlayerName)
                    .Select(l => l.VesselId)
                    .Union(LockSystem.LockQuery.GetAllUnloadedUpdateLocks(SettingsSystem.CurrentSettings.PlayerName)
                        .Select(l => l.VesselId));
            }

            return FlightGlobals.Vessels
                .Where(v => v!= null && v.id != FlightGlobals.ActiveVessel?.id &&
                            LockSystem.LockQuery.UpdateLockBelongsToPlayer(v.id, SettingsSystem.CurrentSettings.PlayerName) &&
                            (!v.loaded || v.state == Vessel.State.DEAD ||
                            SafetyBubbleSystem.Singleton.IsInSafetyBubble(v)))
                .Select(v => v.id);
        }

        #endregion
    }
}
