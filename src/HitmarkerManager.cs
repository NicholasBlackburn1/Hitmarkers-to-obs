﻿using UnityEngine;

using StressLevelZero.AI;
using StressLevelZero.Combat;
using PuppetMasta;

using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MelonLoader.TinyJSON;
using Newtonsoft.Json.Linq;
using System;

namespace NEP.Hitmarkers
{
    [MelonLoader.RegisterTypeInIl2Cpp]
    public class HitmarkerManager : MonoBehaviour
    {
        public HitmarkerManager(System.IntPtr ptr) : base(ptr) { }

        public static HitmarkerManager _instance;

        public static List<Hitmarker> regularHitmarkerPool;
        public static List<Hitmarker> finisherHitmarkerPool;

        public static List<BehaviourBaseNav> deadNPCs;
        public static Enemy_Turret lastDeadTurret;

        public float hitmarkerScale = 1f;
        public float distanceFromShot;
        public float hitmarkerDistanceScale = 0.15f;
        public float hitmarkerDistanceUntilScale = 10f;
        public float hitmarkerAudio = 1f;
        public float animationSpeed = 1f;
        public bool useDeathSkull;
        public Color hitmarkerColor;
        public Color hitmarkerSkullColor;

        private int hitmarkerPoolCount = 32;

        public static Hitmarker lastHitmarker { get; set; }

        private void Awake()
        {
            if(_instance == null)
            {
                _instance = this;
            }

            DontDestroyOnLoad(_instance);
        }

        private void Start()
        {
            HitmarkerManager.regularHitmarkerPool = new List<Hitmarker>();
            HitmarkerManager.finisherHitmarkerPool = new List<Hitmarker>();
            deadNPCs = new List<BehaviourBaseNav>();

            new GameObject("Hitmarker Audio").AddComponent<Audio.HitmarkerAudio>();

            Transform regularHitmarkerPool = new GameObject("Regular Hitmarker Pool").transform;
            Transform finisherHitmarkerPool = new GameObject("Finisher Hitmarker Pool").transform;
            regularHitmarkerPool.SetParent(transform);
            finisherHitmarkerPool.SetParent(transform);

            for(int i = 0; i < hitmarkerPoolCount; i++)
            {
                Hitmarker hitmarker = new GameObject($"Regular Hitmarker {i}").AddComponent<Hitmarker>();
                hitmarker.gameObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
                hitmarker.transform.SetParent(regularHitmarkerPool);
                HitmarkerManager.regularHitmarkerPool.Add(hitmarker);
            }

            for(int i = 0; i < hitmarkerPoolCount; i++)
            {
                Hitmarker hitmarker = new GameObject($"Finisher Hitmarker {i}").AddComponent<Hitmarker>();
                hitmarker.gameObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
                hitmarker.transform.SetParent(finisherHitmarkerPool);
                hitmarker.UseFinisherHitmarker(true);
                HitmarkerManager.finisherHitmarkerPool.Add(hitmarker);
            }
        }

        private void Update()
        {
            for (int i = 0; i < deadNPCs.Count; i++)
            {
                if (deadNPCs[i].health.cur_hp > 0f || deadNPCs[i] == null)
                {
                    deadNPCs.RemoveAt(i);
                }
            }
        }

        // From ModThatIsNotMod
        public static Transform GetPlayerHead()
        {
            GameObject rigManager = GameObject.Find("[RigManager (Default Brett)]");

            if (rigManager != null)
            {
                return rigManager.transform.Find("[PhysicsRig]/Head/PlayerTrigger");
            }

            return null;
        }

        public void OnProjectileCollision(TriggerRefProxy playerProxy, Collider collider, Vector3 impactWorld, Vector3 impactNormal)
        {
            if (!HitmarkersMain.enableMod) { return; }

            try
            {
                distanceFromShot = (impactWorld - GetPlayerHead().position).magnitude;

                if (collider.gameObject.layer != 12 || playerProxy.triggerType != TriggerRefProxy.TriggerType.Player) { return; }
                //if (playerProxy.root.name != "[RigManager (Default Brett)]") { return; }

                EvaluateEntanglementPlayer(playerProxy, collider, impactWorld);
                EvaluateNPC(collider.transform, impactWorld);
            }
            catch { }
        }

        public void OnStabJointCreated(StabSlash.StabPoint stabPoint, ConfigurableJoint joint)
        {
            if(stabPoint.stabJoints[0] != null)
            {
                EvaluateNPC(stabPoint.stabJoints[0].collider.transform, stabPoint.pointTran.position);
            }
        }

        private void EvaluateEntanglementPlayer(TriggerRefProxy proxy, Collider collider, Vector3 impactWorld)
        {
            Transform playerRepRoot = collider.transform.root;

            // Simple Entanglement support
            if (playerRepRoot.name.StartsWith("PlayerRep"))
            {
                SpawnHitmarker(false, impactWorld);
            }
        }
        private string deaths(int kills)
        {
            var data = new[] {

                new {total = kills}
            };

            var json = JArray.FromObject(data)[0].ToString();
            return json;
        }
        // so i can send it this shit to obs
        public async Task sendkillsAsync(string data)
        {
            try
            {
                MelonLogger.Msg("trying to send data to server");

                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:5000/setkills");
                httpWebRequest.ContentType = "application/json; charset=utf-8";
                httpWebRequest.Method = "POST";

                var json = JSON.Load(data);

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {

                    streamWriter.WriteAsync(json);
                    streamWriter.Flush();
                    streamWriter.Close();
                }
           
            using (var response = httpWebRequest.GetResponse() as HttpWebResponse)
            {
                if (httpWebRequest.HaveResponse && response != null)
                {
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {

                        MelonLogger.Msg(reader.ReadToEnd());
                    }
                }
            }
            }
            catch (Exception e)
            {
                MelonLogger.Msg(e.Message);
            }
        }

        private int killed = 0;

        private void EvaluateNPC(Transform transform, Vector3 impactWorld)
        {
            MelonLoader.MelonLogger.Msg(transform.name);
            AIBrain brain = transform.GetComponentInParent<AIBrain>();

            if (brain == null) { return; }

            BehaviourBaseNav navBehaviour = brain.behaviour;
           
            if (navBehaviour.puppetMaster.isDead || navBehaviour == deadNPCs.FirstOrDefault((npc) => npc == navBehaviour)) {
                killed += 1;
                MelonLogger.Msg("u killed about " + killed);
                sendkillsAsync(deaths(killed));
                return; }

            SpawnHitmarker(navBehaviour.puppetMaster.isKilling, impactWorld);
        }

        public static void SpawnHitmarker(bool isFinisher, Vector3 position)
        {
            if (!HitmarkersMain.enableMod) { return; }

            Hitmarker hitmarker = isFinisher ? finisherHitmarkerPool.FirstOrDefault((marker) => !marker.gameObject.active) : regularHitmarkerPool.FirstOrDefault((marker) => !marker.gameObject.active);

            lastHitmarker = hitmarker;

            SetupHitmarker(hitmarker, position);

            hitmarker.gameObject.SetActive(true);
        }

        private static void SetupHitmarker(Hitmarker hitmarker, Vector3 position)
        {
            hitmarker.transform.position = position;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(StabSlash.StabPoint))]
    [HarmonyLib.HarmonyPatch(nameof(StabSlash.StabPoint.JointSetup))]
    public static class StabPatch
    {
        public static void Postfix(StabSlash.StabPoint __instance, ConfigurableJoint j)
        {
            HitmarkerManager._instance.OnStabJointCreated(__instance, j);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Projectile))]
    [HarmonyLib.HarmonyPatch(nameof(Projectile.Awake))]
    public static class ProjectilePatch
    {
        public static void Prefix(Projectile __instance)
        {
            __instance.onCollision.AddListener(
                new System.Action<Collider, Vector3, Vector3>
                (((Collider collider, Vector3 world, Vector3 normal)
                =>
                {
                    HitmarkerManager._instance.OnProjectileCollision(__instance._proxy, collider, world, normal);
                })));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(BehaviourBaseNav))]
    [HarmonyLib.HarmonyPatch(nameof(BehaviourBaseNav.KillStart))]
    public static class BehaviourPatch
    {
        public static void Postfix(BehaviourBaseNav __instance)
        {
            HitmarkerManager.deadNPCs.Add(__instance);

            if(HitmarkerManager.lastHitmarker != null)
            {
                HitmarkerManager.lastHitmarker.gameObject.SetActive(false);
                HitmarkerManager.SpawnHitmarker(true, HitmarkerManager.lastHitmarker.transform.position);
                HitmarkerManager.lastHitmarker = null;
            }
        }
    }
}
