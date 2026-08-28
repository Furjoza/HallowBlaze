using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class PlayModeInfrastructureTests
    {
        [UnityTest]
        public IEnumerator PlayModeAssemblyLoads()
        {
            yield return null;
            Assert.That(
                typeof(PlayModeInfrastructureTests).GetTypeInfo().Assembly.GetName().Name,
                Is.EqualTo("HallowBlaze.Tests.PlayMode"));
        }

        [UnityTest]
        public IEnumerator PlayerMoveResolvesOnce()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            Type playerType = gameAssembly.GetType("PlayerScript");
            Type wallType = gameAssembly.GetType("Wall");
            Type soundManagerType = gameAssembly.GetType("SoundManager");
            Assert.That(playerType, Is.Not.Null);
            Assert.That(wallType, Is.Not.Null);
            Assert.That(soundManagerType, Is.Not.Null);

            GameObject playerObject = new GameObject("HB-000G Player");
            GameObject wallObject = new GameObject("HB-000G One-hit Wall");
            GameObject soundObject = new GameObject("HB-000G SoundManager");
            AudioClip wallSound = AudioClip.Create("HB-000G Wall Sound", 1, 1, 44100, false);

            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            FieldInfo moveTimeField = playerType.BaseType.GetField("moveTime");
            moveTimeField.SetValue(player, 0.01f);
            FieldInfo blockingLayerField = playerType.BaseType.GetField("blockingLayer");
            blockingLayerField.SetValue(player, (LayerMask)(1 << 8));

            MethodInfo startMethod = playerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
            startMethod.Invoke(player, null);
            FieldInfo foodField = playerType.GetField("food", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo healthField = playerType.GetField("health", BindingFlags.Instance | BindingFlags.NonPublic);
            foodField.SetValue(player, 10);
            healthField.SetValue(player, 10);

            wallObject.layer = 8;
            wallObject.transform.position = Vector3.right;
            wallObject.AddComponent<BoxCollider2D>();
            wallObject.AddComponent<SpriteRenderer>();
            Component wall = wallObject.AddComponent(wallType);
            wallType.GetField("hp").SetValue(wall, 1);
            wallType.GetField("chopSound1").SetValue(wall, wallSound);
            wallType.GetField("chopSound2").SetValue(wall, wallSound);

            AudioSource audioSource = soundObject.AddComponent<AudioSource>();
            Component soundManager = soundObject.AddComponent(soundManagerType);
            soundManagerType.GetField("efxSource").SetValue(soundManager, audioSource);
            ((Behaviour)soundManager).enabled = false;
            Physics2D.SyncTransforms();

            MethodInfo attemptMoveMethod = playerType.GetMethod("AttemptMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo closedAttemptMoveMethod = attemptMoveMethod.MakeGenericMethod(wallType);
            closedAttemptMoveMethod.Invoke(player, new object[] { 1, 0 });

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(wallObject.activeSelf, Is.False, "The accepted wall interaction should resolve once.");
            Assert.That(foodField.GetValue(player), Is.EqualTo(9), "The accepted action should spend one food.");
            Assert.That(player.transform.position.x, Is.EqualTo(0f).Within(0.01f),
                "One input must not enter a tile freed by the same wall interaction.");

            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(wallObject);
            UnityEngine.Object.DestroyImmediate(soundObject);
            UnityEngine.Object.DestroyImmediate(wallSound);
        }

        [UnityTest]
        public IEnumerator RejectedPlayerMoveDoesNotSpendFood()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            Type playerType = gameAssembly.GetType("PlayerScript");
            Type wallType = gameAssembly.GetType("Wall");

            GameObject playerObject = new GameObject("HB-000G Blocked Player");
            GameObject obstacleObject = new GameObject("HB-000G Obstacle");
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            playerType.BaseType.GetField("moveTime").SetValue(player, 0.01f);
            playerType.BaseType.GetField("blockingLayer").SetValue(player, (LayerMask)(1 << 8));
            playerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);

            FieldInfo foodField = playerType.GetField("food", BindingFlags.Instance | BindingFlags.NonPublic);
            foodField.SetValue(player, 10);
            obstacleObject.layer = 8;
            obstacleObject.transform.position = Vector3.right;
            obstacleObject.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();

            MethodInfo attemptMoveMethod = playerType.GetMethod("AttemptMove", BindingFlags.Instance | BindingFlags.NonPublic);
            attemptMoveMethod.MakeGenericMethod(wallType).Invoke(player, new object[] { 1, 0 });
            yield return null;

            Assert.That(foodField.GetValue(player), Is.EqualTo(10));
            Assert.That(player.transform.position.x, Is.EqualTo(0f).Within(0.01f));

            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(obstacleObject);
        }

        [UnityTest]
        public IEnumerator FreePlayerMoveSpendsFoodAndPlaysSound()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            Type playerType = gameAssembly.GetType("PlayerScript");
            Type wallType = gameAssembly.GetType("Wall");
            Type soundManagerType = gameAssembly.GetType("SoundManager");

            GameObject playerObject = new GameObject("HB-000G Free Player");
            GameObject soundObject = new GameObject("HB-000G Movement SoundManager");
            AudioClip moveSound = AudioClip.Create("HB-000G Move Sound", 4410, 1, 44100, false);
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            playerType.BaseType.GetField("moveTime").SetValue(player, 0f);
            playerType.BaseType.GetField("blockingLayer").SetValue(player, (LayerMask)(1 << 8));
            playerType.GetField("moveSound1").SetValue(player, moveSound);
            playerType.GetField("moveSound2").SetValue(player, moveSound);
            playerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);

            FieldInfo foodField = playerType.GetField("food", BindingFlags.Instance | BindingFlags.NonPublic);
            foodField.SetValue(player, 10);
            playerType.GetField("health", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 10);

            AudioSource audioSource = soundObject.AddComponent<AudioSource>();
            Component soundManager = soundObject.AddComponent(soundManagerType);
            soundManagerType.GetField("efxSource").SetValue(soundManager, audioSource);
            ((Behaviour)soundManager).enabled = false;

            MethodInfo attemptMoveMethod = playerType.GetMethod("AttemptMove", BindingFlags.Instance | BindingFlags.NonPublic);
            attemptMoveMethod.MakeGenericMethod(wallType).Invoke(player, new object[] { 1, 0 });

            int remainingFrames = 30;
            while (Mathf.Abs(player.transform.position.x - 1f) > 0.01f && remainingFrames-- > 0)
                yield return null;

            Assert.That(foodField.GetValue(player), Is.EqualTo(9));
            Assert.That(player.transform.position.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(audioSource.clip, Is.SameAs(moveSound));

            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(soundObject);
            UnityEngine.Object.DestroyImmediate(moveSound);
        }
    }
}
