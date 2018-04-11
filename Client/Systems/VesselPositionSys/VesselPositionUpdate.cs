﻿using LunaClient.VesselUtilities;
using LunaCommon;
using System;
using UnityEngine;

namespace LunaClient.Systems.VesselPositionSys
{
    /// <summary>
    /// This class handle the vessel position updates that we received and applies it to the correct vessel. 
    /// It also handle it's interpolations
    /// </summary>
    public class VesselPositionUpdate
    {
        #region Fields

        private Vessel _vessel;
        public Vessel Vessel
        {
            get
            {
                if (_vessel == null)
                    _vessel = FlightGlobals.FindVessel(VesselId);
                return _vessel;
            }
        }

        public CelestialBody Body => LerpPercentage < 0.5 ? GetBody(BodyIndex) : GetBody(Target.BodyIndex);
        public VesselPositionUpdate Target { get; set; }

        private bool CurrentlySpectatingThisVessel => VesselCommon.IsSpectating && FlightGlobals.ActiveVessel.id == VesselId;

        #region Message Fields

        public Guid VesselId { get; set; }
        public int BodyIndex { get; set; }
        public double[] LatLonAlt { get; set; } = new double[3];
        public double[] NormalVector { get; set; } = new double[3];
        public double[] TransformPosition { get; set; } = new double[3];
        public double[] Velocity { get; set; } = new double[3];
        public double[] OrbitPos { get; set; } = new double[3];
        public double[] OrbitVel { get; set; } = new double[3];
        public double[] Orbit { get; set; } = new double[8];
        public float[] SrfRelRotation { get; set; } = new float[4];

        public float HeightFromTerrain;

        public long SentTimeStamp { get; set; }
        public long ReceiveTimeStamp { get; set; }
        public double GameTimeStamp { get; set; }

        #endregion

        #region Vessel position information fields

        public Quaternion SurfaceRelRotation => new Quaternion(SrfRelRotation[0], SrfRelRotation[1], SrfRelRotation[2], SrfRelRotation[3]);
        public Vector3 Normal => new Vector3d(NormalVector[0], NormalVector[1], NormalVector[2]);
        public Vector3d VelocityVector => new Vector3d(Velocity[0], Velocity[1], Velocity[2]);
        public Vector3d Transform => new Vector3d(TransformPosition[0], TransformPosition[1], TransformPosition[2]);

        #endregion

        #region Interpolation fields

        public bool InterpolationFinished => LerpPercentage >= 1;
        public float InterpolationDuration => (float)(Target.GameTimeStamp - GameTimeStamp);

        private float _lerpPercentage = 1;
        public float LerpPercentage
        {
            get => Mathf.Clamp01(_lerpPercentage);
            set => _lerpPercentage = value;
        }

        #endregion

        #endregion

        #region Main method

        /// <summary>
        /// Call this method to reset a vessel position update so you can reuse it when NOT using interpolation
        /// </summary>
        public void SetTarget(VesselPositionUpdate target)
        {
            Target = target;
            LerpPercentage = 1;
        }

        /// <summary>
        /// Call this method to apply a vessel update using interpolation
        /// </summary>
        public void ApplyInterpolatedVesselUpdate()
        {
            if (Vessel == null || Vessel.precalc == null || Vessel.state == Vessel.State.DEAD)
            {
                VesselPositionSystem.VesselsToRemove.Enqueue(VesselId);
                return;
            }

            if (InterpolationFinished)
            {
                if (VesselPositionSystem.TargetVesselUpdateQueue[VesselId].TryDequeue(out var targetUpdate))
                {
                    if (Target == null)
                    {
                        //We are in the first iteration of the interpolation (we just started to apply vessel updates)
                        Target = targetUpdate;
                        return;
                    }

                    ProcessRestart();
                    LerpPercentage = 0;

                    SentTimeStamp = Target?.SentTimeStamp ?? SentTimeStamp;
                    ReceiveTimeStamp = Target?.ReceiveTimeStamp ?? ReceiveTimeStamp;
                    GameTimeStamp = Target?.GameTimeStamp ?? GameTimeStamp;

                    Target = targetUpdate;

                    UpdateProtoVesselValues();
                }
                else
                {
                    LunaLog.LogWarning("No updates in queue!");
                }
            }

            if (Body == null)
            {
                VesselPositionSystem.VesselsToRemove.Enqueue(VesselId);
                return;
            }

            try
            {
                if (Vessel.isEVA)
                    ApplyInterpolationToEva(LerpPercentage);
                else
                    ApplyInterpolations(LerpPercentage);
            }
            catch
            {
                // ignored
            }
            finally
            {
                LerpPercentage += Time.fixedDeltaTime / InterpolationDuration;
            }
        }

        /// <summary>
        /// Call this method to apply a vessel update either by interpolation or just by raw positioning the vessel
        /// </summary>
        public void ApplyVesselUpdate()
        {
            if (Target == null) return;
            if (Body == null || Vessel == null || Vessel.precalc == null || Vessel.state == Vessel.State.DEAD)
            {
                VesselPositionSystem.VesselsToRemove.Enqueue(VesselId);
                return;
            }

            UpdateProtoVesselValues();

            try
            {
                if (Vessel.isEVA)
                    ApplyInterpolationToEva(1);
                else
                    ApplyInterpolations(1);
            }
            catch
            {
                // ignored
            }
        }

        #endregion

        #region Private

        private void UpdateProtoVesselValues()
        {
            Vessel.protoVessel.latitude = Target.LatLonAlt[0];
            Vessel.protoVessel.longitude = Target.LatLonAlt[1];
            Vessel.protoVessel.altitude = Target.LatLonAlt[2];
            Vessel.protoVessel.height = Target.HeightFromTerrain;

            Vessel.protoVessel.normal[0] = Target.Normal[0];
            Vessel.protoVessel.normal[1] = Target.Normal[1];
            Vessel.protoVessel.normal[2] = Target.Normal[2];

            Vessel.protoVessel.rotation[0] = Target.SrfRelRotation[0];
            Vessel.protoVessel.rotation[1] = Target.SrfRelRotation[1];
            Vessel.protoVessel.rotation[2] = Target.SrfRelRotation[2];
            Vessel.protoVessel.rotation[3] = Target.SrfRelRotation[3];

            Vessel.protoVessel.orbitSnapShot.inclination = Target.Orbit[0];
            Vessel.protoVessel.orbitSnapShot.eccentricity = Target.Orbit[1];
            Vessel.protoVessel.orbitSnapShot.semiMajorAxis = Target.Orbit[2];
            Vessel.protoVessel.orbitSnapShot.LAN = Target.Orbit[3];
            Vessel.protoVessel.orbitSnapShot.argOfPeriapsis = Target.Orbit[4];
            Vessel.protoVessel.orbitSnapShot.meanAnomalyAtEpoch = Target.Orbit[5];
            Vessel.protoVessel.orbitSnapShot.epoch = Target.Orbit[6];
            Vessel.protoVessel.orbitSnapShot.ReferenceBodyIndex = (int)Target.Orbit[7];
        }

        private void ApplyInterpolationsToLoadedVessel(float lerpPercentage)
        {
            var currentSurfaceRelRotation = Quaternion.Slerp(SurfaceRelRotation, Target.SurfaceRelRotation, lerpPercentage);
            var curVelocity = Vector3d.Lerp(VelocityVector, Target.VelocityVector, lerpPercentage);
            
            //Always apply velocity otherwise vessel is not positioned correctly and sometimes it moves even if it should be stopped.
            Vessel.SetWorldVelocity(curVelocity);
            Vessel.velocityD = curVelocity;

            //If you don't set srfRelRotation and vessel is packed it won't change it's rotation
            Vessel.srfRelRotation = currentSurfaceRelRotation;
            Vessel.SetRotation((Quaternion)Vessel.mainBody.rotation * currentSurfaceRelRotation, true);

            Vessel.checkLanded();
            Vessel.checkSplashed();

            //Set the position of the vessel based on the orbital parameters
            Vessel.orbitDriver.updateFromParameters();

            if (Vessel.LandedOrSplashed)
            {
                //This updates the Vessel.latitude, Vessel.lon, Vessel.alt
                Vessel.UpdatePosVel();
                    
                Vessel.altitude = LunaMath.Lerp(LatLonAlt[2], Target.LatLonAlt[2], lerpPercentage);
                Vessel.SetPosition(Body.GetWorldSurfacePosition(Vessel.latitude, Vessel.longitude, Vessel.altitude));
            }

            foreach (var part in Vessel.Parts)
                part.ResumeVelocity();

            if (CurrentlySpectatingThisVessel)
            {
                Vessel.UpdatePosVel();
                Vessel.precalc.CalculatePhysicsStats(); //This will update the localCom and other variables of the vessel
            }
        }

        private void ApplyInterpolations(float lerpPercentage)
        {
            ApplyOrbitInterpolation(lerpPercentage);

            //Do not use CoM. It's not needed and it generate issues when you patch the protovessel with it as it generate weird commnet lines
            //It's important to set the static pressure as otherwise the vessel situation is not updated correctly when
            //Vessel.updateSituation() is called in the Vessel.LateUpdate(). Same applies for landed and splashed
            Vessel.staticPressurekPa = FlightGlobals.getStaticPressure(Target.LatLonAlt[2], Vessel.mainBody);
            Vessel.heightFromTerrain = Target.HeightFromTerrain;

            if (!Vessel.loaded)
            {
                //DO NOT lerp the latlonalt as otherwise if you are in orbit you will see landed vessels in the map view with weird jittering
                Vessel.latitude = Target.LatLonAlt[0];
                Vessel.longitude = Target.LatLonAlt[1];
                Vessel.altitude = Target.LatLonAlt[2];
                Vessel.orbitDriver.updateFromParameters();

                if (Vessel.LandedOrSplashed)
                    Vessel.SetPosition(Body.GetWorldSurfacePosition(Vessel.latitude, Vessel.longitude, Vessel.altitude));
            }
            else
            {
                ApplyInterpolationsToLoadedVessel(lerpPercentage);
            }
        }

        private void ApplyInterpolationToEva(float lerpPercentage)
        {
            Vessel.latitude = Target.LatLonAlt[0];
            Vessel.longitude = Target.LatLonAlt[1];
            Vessel.altitude = Target.LatLonAlt[2];

            var currentSurfaceRelRotation = Quaternion.Lerp(SurfaceRelRotation, Target.SurfaceRelRotation, lerpPercentage);
            Vessel.SetRotation((Quaternion)Vessel.mainBody.rotation * currentSurfaceRelRotation, true);
            Vessel.srfRelRotation = currentSurfaceRelRotation;

            ApplyOrbitInterpolation(lerpPercentage);
            Vessel.orbitDriver.updateFromParameters();

            //We don't do the surface positioning as with vessels because kerbals don't walk at high speeds and with this code it will be enough ;)
            if (Vessel.situation < Vessel.Situations.FLYING)
                Vessel.SetPosition(Body.GetWorldSurfacePosition(Vessel.latitude, Vessel.longitude, Vessel.altitude));
        }

        private void ApplyOrbitInterpolation(float lerpPercentage)
        {
            var lerpTime = LunaMath.Lerp(GameTimeStamp, Target.GameTimeStamp, lerpPercentage);

            var currentOrbit = new Orbit(Orbit[0], Orbit[1], Orbit[2], Orbit[3], Orbit[4], Orbit[5], Orbit[6], Body);
            var targetOrbit = new Orbit(Target.Orbit[0], Target.Orbit[1], Target.Orbit[2], Target.Orbit[3], Target.Orbit[4], Target.Orbit[5], Target.Orbit[6], Body);

            var targetPos = (targetOrbit.getTruePositionAtUT(lerpTime) - Body.getTruePositionAtUT(lerpTime)).xzy;
            var currentPos = (currentOrbit.getTruePositionAtUT(lerpTime) - Body.getTruePositionAtUT(lerpTime)).xzy;

            var targetVel = targetOrbit.getOrbitalVelocityAtUT(lerpTime) + targetOrbit.referenceBody.GetFrameVelAtUT(lerpTime) - Body.GetFrameVelAtUT(lerpTime);
            var currentVel = currentOrbit.getOrbitalVelocityAtUT(lerpTime) + currentOrbit.referenceBody.GetFrameVelAtUT(lerpTime) - Body.GetFrameVelAtUT(lerpTime);

            var lerpedPos = Vector3d.Lerp(currentPos, targetPos, lerpPercentage);
            var lerpedVel = Vector3d.Lerp(currentVel, targetVel, lerpPercentage);

            Vessel.orbitDriver.orbit.UpdateFromStateVectors(lerpedPos, lerpedVel, Body, lerpTime);
        }
        
        /// <summary>
        /// Here we apply the CURRENT vessel position to this update.
        /// </summary>
        private void ProcessRestart()
        {
            TransformPosition[0] = Vessel.ReferenceTransform.position.x;
            TransformPosition[1] = Vessel.ReferenceTransform.position.y;
            TransformPosition[2] = Vessel.ReferenceTransform.position.z;

            SrfRelRotation[0] = Vessel.srfRelRotation.x;
            SrfRelRotation[1] = Vessel.srfRelRotation.y;
            SrfRelRotation[2] = Vessel.srfRelRotation.z;
            SrfRelRotation[3] = Vessel.srfRelRotation.w;

            Vector3d srfVel = Quaternion.Inverse(Body.bodyTransform.rotation) * Vessel.srf_velocity;
            Velocity[0] = srfVel.x;
            Velocity[1] = srfVel.y;
            Velocity[2] = srfVel.z;

            LatLonAlt[0] = Vessel.latitude;
            LatLonAlt[1] = Vessel.longitude;
            LatLonAlt[2] = Vessel.altitude;

            NormalVector[0] = Vessel.terrainNormal.x;
            NormalVector[1] = Vessel.terrainNormal.y;
            NormalVector[2] = Vessel.terrainNormal.z;

            Orbit[0] = Vessel.orbit.inclination;
            Orbit[1] = Vessel.orbit.eccentricity;
            Orbit[2] = Vessel.orbit.semiMajorAxis;
            Orbit[3] = Vessel.orbit.LAN;
            Orbit[4] = Vessel.orbit.argumentOfPeriapsis;
            Orbit[5] = Vessel.orbit.meanAnomalyAtEpoch;
            Orbit[6] = Vessel.orbit.epoch;
            Orbit[7] = Vessel.orbit.referenceBody.flightGlobalsIndex;

            HeightFromTerrain = Vessel.heightFromTerrain;
        }

        #region Helper methods

        private static CelestialBody GetBody(int bodyIndex)
        {
            try
            {
                return FlightGlobals.Bodies[bodyIndex];
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #endregion
    }
}
