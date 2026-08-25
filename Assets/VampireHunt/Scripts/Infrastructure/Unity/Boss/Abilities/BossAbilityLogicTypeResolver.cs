using System;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    internal static class BossAbilityLogicTypeResolver
    {
        public static Func<IBossAbilityLogicRuntime> CreateFactory(
            string assemblyQualifiedTypeName,
            BossAbilityTuning tuning)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
                throw new InvalidOperationException("The Boss Ability has no Logic script assigned.");

            Type logicType = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            if (logicType == null)
            {
                string fullTypeName = assemblyQualifiedTypeName.Split(',')[0].Trim();
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    logicType = assembly.GetType(fullTypeName, throwOnError: false);
                    if (logicType != null) break;
                }
            }

            if (logicType == null)
                throw new InvalidOperationException(
                    $"Boss Ability Logic type could not be loaded: {assemblyQualifiedTypeName}");
            if (!typeof(IBossAbilityLogicRuntime).IsAssignableFrom(logicType) ||
                logicType.IsAbstract || logicType.IsInterface)
                throw new InvalidOperationException(
                    $"{logicType.FullName} must be a concrete {nameof(IBossAbilityLogicRuntime)} script.");
            if (logicType.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException(
                    $"{logicType.FullName} must have a public parameterless constructor.");

            BossAbilityTuning validated = (tuning ?? new BossAbilityTuning()).CloneValidated();
            return () =>
            {
                var runtime = (IBossAbilityLogicRuntime)Activator.CreateInstance(logicType);
                if (runtime is IBossAbilityTuningConsumer consumer)
                    consumer.BindTuning(validated.CloneValidated());
                return runtime;
            };
        }
    }
}
