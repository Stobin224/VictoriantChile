using System;
using System.Linq;
using NUnit.Framework;
using VictoriantChile.Simulation.Core;

namespace VictoriantChile.Simulation.Tests.EditMode
{
    public sealed class HeadlessAssemblyContractTests
    {
        [Test]
        public void CoreAssemblyCanBeReferenced()
        {
            Assert.That(typeof(HeadlessAssemblyInfo).Assembly.GetName().Name, Is.EqualTo("VictoriantChile.Simulation.Core"));
        }

        [Test]
        public void MarkerExposesExpectedContract()
        {
            Assert.That(HeadlessAssemblyInfo.ContractName, Is.EqualTo("VictoriantChile.Simulation.Core.HeadlessHarness"));
            Assert.That(HeadlessAssemblyInfo.ContractVersion, Is.EqualTo(1));
        }

        [Test]
        public void CoreAssemblyDoesNotReferenceUnityAssemblies()
        {
            string[] forbiddenReferences = typeof(HeadlessAssemblyInfo)
                .Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name != null && (name.StartsWith("UnityEngine", StringComparison.Ordinal) || name.StartsWith("UnityEditor", StringComparison.Ordinal)))
                .ToArray();

            Assert.That(forbiddenReferences, Is.Empty);
        }
    }
}
