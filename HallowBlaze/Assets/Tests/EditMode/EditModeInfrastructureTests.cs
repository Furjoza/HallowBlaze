using System.Reflection;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class EditModeInfrastructureTests
    {
        [Test]
        public void EditModeAssemblyLoads()
        {
            Assert.That(
                typeof(EditModeInfrastructureTests).GetTypeInfo().Assembly.GetName().Name,
                Is.EqualTo("HallowBlaze.Tests.EditMode"));
        }
    }
}
