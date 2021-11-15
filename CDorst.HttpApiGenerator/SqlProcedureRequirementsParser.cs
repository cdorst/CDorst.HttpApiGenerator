using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CDorst.HttpApiGenerator;

internal static class SqlProcedureRequirementsParser
{
    private static readonly char[] _lineSeparator = { '\n' };
    private static readonly char[] _spaceSeparator = { ' ' };

    public static void ParseProcedureRequirements(string sqlCodeBlock, out string? procedureName, out string? codeName, out int? cacheDurationSeconds, out int? cacheDurationMinutes, out int? cacheDurationHours, out bool useCache, out bool useHttpGet, out List<ProcedureParameter>? parameters, out List<KeyValuePair<string, string>>? returns, out List<KeyValuePair<int, string>>? errors)
    {
        procedureName = default(string?);
        codeName = default(string?);
        cacheDurationSeconds = default(int?);
        cacheDurationMinutes = default(int?);
        cacheDurationHours = default(int?);
        useCache = false;
        useHttpGet = false;
        parameters = default(List<ProcedureParameter>);
        returns = default(List<KeyValuePair<string, string>>);
        errors = default(List<KeyValuePair<int, string>>);
        var lines = sqlCodeBlock.Split(_lineSeparator, StringSplitOptions.None).Select(ln => ln.TrimEnd()).ToArray();
        var lineCount = lines.Count();
        var processedLines = new HashSet<int>();

        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            if (processedLines.Contains(lineIndex)) continue;

            var line = lines[lineIndex];

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("```"))
            {
                continue;
            }

            var lineUpper = line.ToUpper();

            if (lineUpper.StartsWith("PROCEDURE "))
            {
                procedureName = line.Split(_spaceSeparator, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                codeName = procedureName.Replace("[", "").Replace("]", "").Replace(".", "");
                continue;
            }

            if (lineUpper == "HTTP GET")
            {
                useHttpGet = true;
                continue;
            }

            if (lineUpper.StartsWith("CACHE "))
            {
                var split = lineUpper.Split(_spaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                var time = int.Parse(split[1]);

                useCache = true;

                switch (split[2][0])
                {
                    case 'S':
                        cacheDurationSeconds = time;
                        continue;
                    case 'M':
                        cacheDurationMinutes = time;
                        continue;
                    case 'H':
                        cacheDurationHours = time;
                        continue;
                    default:
                        throw new InvalidOperationException($"Error parsing cache info: \"{line}\"");
                }
            }

            if (line[0] == '@')
            {
                var paramLineSplit = line.Split(_spaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                var paramLineSplitLength = paramLineSplit.Length;
                var paramName = paramLineSplit[0].Substring(1);
                var paramType = default(string?);
                var paramStringLength = default(int?);
                var paramRequired = false;
                var paramPositive = false;
                var paramDefaultValue = default(string?);
                var paramTableParameters = default(List<ProcedureParameter>);

                if (paramLineSplit[paramLineSplitLength - 1][0] == '{') // tvp
                {
                    paramTableParameters = new List<ProcedureParameter>();
                    paramType = paramLineSplit[1];


                    // iterate over remaining lines
                    for (var innerLineIndex = lineIndex + 1; innerLineIndex < lineCount; innerLineIndex++)
                    {
                        var innerLine = lines[innerLineIndex].TrimStart();

                        if (string.IsNullOrWhiteSpace(innerLine))
                        {
                            processedLines.Add(innerLineIndex);
                            break;
                        }
                        else if (innerLine[0] == '}')
                        {
                            if (innerLine.ToUpper() == "} REQUIRED")
                            {
                                paramRequired = true;
                            }

                            processedLines.Add(innerLineIndex);
                            break;
                        }

                        var innerLineSplit = innerLine.Split(_spaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                        var innerParamType = default(string?);
                        var innerParamStringLength = default(int?);
                        var innerParamRequired = false;
                        var innerParamPositive = false;
                        var innerParamDefaultValue = default(string?);

                        for (var splitIndex = 2; splitIndex < innerLineSplit.Length; splitIndex++)
                        {
                            var splitUpper = innerLineSplit[splitIndex].ToUpper();
                            switch (splitUpper)
                            {
                                case "REQUIRED":
                                    innerParamRequired = true;
                                    continue;
                                case "POSITIVE":
                                    innerParamPositive = true;
                                    continue;
                                default:
                                    if (splitUpper.StartsWith("DEFAULT("))
                                    {
                                        innerParamDefaultValue = splitUpper.Substring(8).Replace(")", "");
                                    }
                                    break;
                            }
                        }

                        innerParamType = innerLineSplit[1].ToSystemTypeNullable();
                        if (innerParamType == "string?")
                        {
                            var stringSplit = innerLineSplit[1].Split('(');
                            if (stringSplit.Length > 1 && stringSplit[1] != "MAX)" && int.TryParse(stringSplit[1].Replace(")", ""), out var stringLength))
                            {
                                innerParamStringLength = stringLength;
                            }
                        }

                        paramTableParameters.Add(new ProcedureParameter(innerLineSplit[0], innerParamType, innerParamStringLength, innerParamRequired, innerParamPositive, innerParamDefaultValue));

                        processedLines.Add(innerLineIndex);
                    }
                }
                else // not tvp
                {

                    for (var splitIndex = 2; splitIndex < paramLineSplitLength; splitIndex++)
                    {
                        var splitUpper = paramLineSplit[splitIndex].ToUpper();
                        switch (splitUpper)
                        {
                            case "REQUIRED":
                                paramRequired = true;
                                continue;
                            case "POSITIVE":
                                paramPositive = true;
                                continue;
                            default:
                                if (splitUpper.StartsWith("DEFAULT("))
                                {
                                    paramDefaultValue = splitUpper.Substring(8).Replace(")", "");
                                }
                                break;
                        }
                    }

                    paramType = paramLineSplit[1].ToSystemTypeNullable();
                    if (paramType == "string?")
                    {
                        var stringSplit = paramLineSplit[1].Split('(');
                        if (stringSplit.Length > 1 && stringSplit[1] != "MAX)" && int.TryParse(stringSplit[1].Replace(")", ""), out var stringLength))
                        {
                            paramStringLength = stringLength;
                        }
                    }
                }



                // add parameter
                if (parameters is null) parameters = new List<ProcedureParameter>();
                parameters.Add(new ProcedureParameter(paramName, paramType, paramStringLength, paramRequired, paramPositive, paramDefaultValue, paramTableParameters));

                continue;
            }

            switch (lineUpper)
            {
                case "RETURNS":
                    // iterate over remaining lines
                    for (var innerLineIndex = lineIndex + 1; innerLineIndex < lineCount; innerLineIndex++)
                    {
                        var innerLine = lines[innerLineIndex];

                        if (string.IsNullOrWhiteSpace(innerLine))
                        {
                            processedLines.Add(innerLineIndex);
                            break;
                        }

                        if (innerLine.Contains(' '))
                        {
                            var innerLineSplit = innerLine.Split(_spaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                            if (innerLineSplit.Length == 2)
                            {
                                if (returns is null) returns = new List<KeyValuePair<string, string>>();
                                returns.Add(new KeyValuePair<string, string>(innerLineSplit[0], innerLineSplit[1].ToSystemTypeNullable()));
                                processedLines.Add(innerLineIndex);
                                continue;
                            }
                        }

                        break;
                    }
                    continue;
                case "CATCH":
                    // iterate over remaining lines
                    for (var innerLineIndex = lineIndex + 1; innerLineIndex < lineCount; innerLineIndex++)
                    {
                        var innerLine = lines[innerLineIndex];

                        if (string.IsNullOrWhiteSpace(innerLine))
                        {
                            processedLines.Add(innerLineIndex);
                            break;
                        }

                        if (innerLine.Length > 4 && innerLine[3] == ' ' && int.TryParse(innerLine.Substring(0, 3), out var statusCode))
                        {
                            if (errors is null) errors = new List<KeyValuePair<int, string>>();
                            errors.Add(new KeyValuePair<int, string>(statusCode, innerLine.Substring(4).Replace("'", "")));
                            processedLines.Add(innerLineIndex);
                        }
                    }
                    continue;
                default:
                    continue;
            }
        }
    }
}
