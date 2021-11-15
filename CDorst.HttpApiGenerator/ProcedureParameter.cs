using System.Collections.Generic;

namespace CDorst.HttpApiGenerator;

internal record struct ProcedureParameter(string Name, string Type, int? StringLength = default, bool Required = false, bool Positive = false, string? DefaultValue = default, IEnumerable<ProcedureParameter>? TableParameters = default);
