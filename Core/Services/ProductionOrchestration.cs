using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class ProductionSequenceStep
    {
        public int Order { get; init; }
        public int MachineId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public PartStatus OutputStatus { get; init; }
    }

    public class OrchestrationStepDefinition
    {
        public int MachineId { get; init; }
        public PartStatus OutputStatus { get; init; }
    }

    public class OrchestrationProfile
    {
        public string Name { get; init; } = "Default";
        public int Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public IReadOnlyList<OrchestrationStepDefinition> Steps { get; init; } = new ReadOnlyCollection<OrchestrationStepDefinition>(Array.Empty<OrchestrationStepDefinition>());

        public OrchestrationProfile Clone(string? name = null, int? version = null)
        {
            return new OrchestrationProfile
            {
                Name = name ?? Name,
                Version = version ?? Version,
                UpdatedAtUtc = DateTime.UtcNow,
                Steps = new ReadOnlyCollection<OrchestrationStepDefinition>(Steps.Select(s => new OrchestrationStepDefinition
                {
                    MachineId = s.MachineId,
                    OutputStatus = s.OutputStatus
                }).ToList())
            };
        }
    }

    public class OrchestrationValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new();
    }

    public class ProductionOrchestration
    {
        private readonly Dictionary<int, Machine> _machinesById;
        private Dictionary<int, ProductionSequenceStep> _stepsByMachineId = new();

        public IReadOnlyList<ProductionSequenceStep> Steps { get; private set; } = new ReadOnlyCollection<ProductionSequenceStep>(new List<ProductionSequenceStep>());
        public OrchestrationProfile ActiveProfile { get; private set; }

        public ProductionOrchestration(IEnumerable<Machine> machines)
        {
            var machineList = machines.ToList();
            _machinesById = machineList.ToDictionary(m => m.MachineId, m => m);
            ActiveProfile = CreateDefaultProfile("Default", 1);
            ApplyProfileUnchecked(ActiveProfile);
        }

        public OrchestrationProfile CreateDefaultProfile(string name, int version)
        {
            var orderedMachineIds = _machinesById.Keys.OrderBy(x => x).ToList();
            return CreateProfileFromMachineOrder(orderedMachineIds, name, version);
        }

        public OrchestrationProfile CreateProfileFromMachineOrder(IReadOnlyList<int> orderedMachineIds, string name = "Edited", int version = 1)
        {
            var defs = new List<OrchestrationStepDefinition>();
            for (int i = 0; i < orderedMachineIds.Count; i++)
            {
                defs.Add(new OrchestrationStepDefinition
                {
                    MachineId = orderedMachineIds[i],
                    OutputStatus = DetermineOutputStatus(i, orderedMachineIds.Count)
                });
            }

            return CreateProfile(defs, name, version);
        }

        public OrchestrationProfile CreateProfile(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, string name = "Edited", int version = 1)
        {
            var defs = stepDefinitions
                .Select(s => new OrchestrationStepDefinition
                {
                    MachineId = s.MachineId,
                    OutputStatus = s.OutputStatus
                })
                .ToList();

            return new OrchestrationProfile
            {
                Name = name,
                Version = version,
                UpdatedAtUtc = DateTime.UtcNow,
                Steps = new ReadOnlyCollection<OrchestrationStepDefinition>(defs)
            };
        }

        public OrchestrationValidationResult ValidateProfile(OrchestrationProfile profile)
        {
            var result = new OrchestrationValidationResult();

            if (profile.Steps.Count == 0)
            {
                result.Errors.Add("Profile contains no sequence steps.");
                return result;
            }

            if (profile.Steps.Count != _machinesById.Count)
            {
                result.Errors.Add($"Profile step count {profile.Steps.Count} does not match machine count {_machinesById.Count}.");
            }

            var machineIds = profile.Steps.Select(s => s.MachineId).ToList();
            var duplicateMachineIds = machineIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateMachineIds.Count > 0)
            {
                result.Errors.Add($"Duplicate machine IDs in profile: {string.Join(",", duplicateMachineIds)}.");
            }

            var unknownMachineIds = machineIds.Where(x => !_machinesById.ContainsKey(x)).Distinct().ToList();
            if (unknownMachineIds.Count > 0)
            {
                result.Errors.Add($"Profile contains unknown machine IDs: {string.Join(",", unknownMachineIds)}.");
            }

            var missingMachineIds = _machinesById.Keys.Where(id => !machineIds.Contains(id)).OrderBy(x => x).ToList();
            if (missingMachineIds.Count > 0)
            {
                result.Errors.Add($"Profile missing machine IDs: {string.Join(",", missingMachineIds)}.");
            }

            for (int i = 0; i < profile.Steps.Count; i++)
            {
                bool isFinal = i == profile.Steps.Count - 1;
                PartStatus output = profile.Steps[i].OutputStatus;
                if (!isFinal && (output == PartStatus.Good || output == PartStatus.Bad))
                {
                    result.Errors.Add($"Step {i} (Machine {profile.Steps[i].MachineId}) has final status {output} before final machine.");
                }
            }

            return result;
        }

        public bool TrySetProfile(OrchestrationProfile profile, out List<string> errors)
        {
            var validation = ValidateProfile(profile);
            errors = validation.Errors;
            if (!validation.IsValid)
            {
                return false;
            }

            ApplyProfileUnchecked(profile);
            return true;
        }

        public int GetNextMachineIndex(int currentMachineId)
        {
            if (!_stepsByMachineId.TryGetValue(currentMachineId, out var step))
            {
                return 0;
            }

            int nextOrder = step.Order + 1;
            return nextOrder >= Steps.Count ? Steps.Count : Steps[nextOrder].MachineId;
        }

        public bool IsFinalMachine(int machineId)
        {
            return _stepsByMachineId.TryGetValue(machineId, out var step) && step.Order == Steps.Count - 1;
        }

        public PartStatus ResolveOutboundStatus(int machineId, Part part)
        {
            if (IsFinalMachine(machineId))
            {
                return part.HasDefect ? PartStatus.Bad : PartStatus.Good;
            }

            if (_stepsByMachineId.TryGetValue(machineId, out var step))
            {
                return step.OutputStatus;
            }

            return PartStatus.InProcess;
        }

        public string DescribeFlow()
        {
            return string.Join(" -> ", Steps.Select(s => $"M{s.MachineId}:{s.MachineName}")) + " -> Exit";
        }

        private void ApplyProfileUnchecked(OrchestrationProfile profile)
        {
            ActiveProfile = profile.Clone(profile.Name, profile.Version);

            var steps = new List<ProductionSequenceStep>();
            for (int i = 0; i < profile.Steps.Count; i++)
            {
                var def = profile.Steps[i];
                steps.Add(new ProductionSequenceStep
                {
                    Order = i,
                    MachineId = def.MachineId,
                    MachineName = _machinesById[def.MachineId].Name,
                    OutputStatus = def.OutputStatus
                });
            }

            Steps = new ReadOnlyCollection<ProductionSequenceStep>(steps);
            _stepsByMachineId = Steps.ToDictionary(s => s.MachineId, s => s);
        }

        private static PartStatus DetermineOutputStatus(int order, int total)
        {
            if (order >= total - 1)
            {
                return PartStatus.Good;
            }

            if (order == 1)
            {
                return PartStatus.Assembled;
            }

            if (order == 2)
            {
                return PartStatus.Tested;
            }

            return PartStatus.InProcess;
        }
    }
}
