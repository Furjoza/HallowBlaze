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
            Assert.That(playerType, Is.Not.Null);
            Assert.That(wallType, Is.Not.Null);

            GameObject playerObject = new GameObject("HB-000G Player");
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            FieldInfo moveTimeField = playerType.BaseType.GetField("moveTime");
            moveTimeField.SetValue(player, 0.01f);

            MethodInfo startMethod = playerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
            startMethod.Invoke(player, null);

            MethodInfo attemptMoveMethod = playerType.GetMethod("AttemptMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo closedAttemptMoveMethod = attemptMoveMethod.MakeGenericMethod(wallType);
            closedAttemptMoveMethod.Invoke(player, new object[] { 1, 0 });

            yield return new WaitForSeconds(0.1f);

            Assert.That(player.transform.position.x, Is.EqualTo(1f).Within(0.01f));
            UnityEngine.Object.Destroy(playerObject);
        }
    }
}
