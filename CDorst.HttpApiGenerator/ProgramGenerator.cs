using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CDorst.HttpApiGenerator;

[Generator]
internal class ProgramGenerator : ISourceGenerator
{
    private static readonly char[] _lineSeparator = { '\n' };
    private static readonly char[] _spaceSeparator = { ' ' };

    public void Execute(GeneratorExecutionContext context)
    {
        var readmeFile = context.AdditionalFiles.Where(file => file.Path.EndsWith("README.md")).FirstOrDefault();
        if (readmeFile is null)
        {
            throw new InvalidOperationException("No README.md additional file found. Add this item <AdditionalFiles Include=\"README.md\" /> to your .csproj file.");
        }

        var readmeText = readmeFile.GetText()?.ToString();
        if (string.IsNullOrWhiteSpace(readmeText))
        {
            throw new InvalidOperationException("README.md must not be empty");
        } 
        else if (!readmeText!.Contains("## Contracts"))
        {
            throw new InvalidOperationException("README.md must contain a \"## Contracts\" section");
        }

        var contractsText = readmeText.Split(new[] { "## Contracts" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(contractsText))
        {
            throw new InvalidOperationException("README.md \"## Contracts\" section must not be blank");
        }

        if (!contractsText.Contains("```sql"))
        {
            throw new InvalidOperationException("README.md \"## Contracts\" section must contain one or more \"```sql\" code blocks");
        }

        var sqlCodeBlocksSplit = contractsText.Split(new[] { "```sql" }, StringSplitOptions.RemoveEmptyEntries);
        var sqlCodeBlocksSplitTrim = sqlCodeBlocksSplit.Where(block => !string.IsNullOrWhiteSpace(block)).Select(block => block.TrimStart());

        var routes = new ConcurrentBag<string>();
        var records = new ConcurrentBag<KeyValuePair<string, string>>();

        Parallel.ForEach(sqlCodeBlocksSplitTrim, sqlCodeBlock =>
        {
            var procedureName = default(string?);
            var codeName = default(string?);

            var cacheDurationSeconds = default(int?);
            var cacheDurationMinutes = default(int?);
            var cacheDurationHours = default(int?);
            var useCache = false;

            var useHttpGet = false;

            var parameters = default(List<ProcedureParameter>);
            var returns = default(List<KeyValuePair<string, string>>);
            var errors = default(List<KeyValuePair<int, string>>);

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

            // generate code for route
            var routeBuilder = new StringBuilder(@$"
app.Map");
            // route: HTTP method & route
            routeBuilder.Append(useHttpGet ? "Get" : "Post");
            routeBuilder.Append($"(\"/{codeName}\", async (");

            // route: lambda input: params
            var parameterCount = (parameters is not null) ? parameters.Count : 0;

            if (useHttpGet)
            {
                for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
                {
                    var parameter = parameters![parameterIndex];
                    routeBuilder.Append($"[FromQuery(Name = \"{parameter.Name}\")] {parameter.Type} request{parameter.Name}, ");
                }
            }
            else
            {
                routeBuilder.Append(codeName).Append("Request request, ");
            }

            // route: lambda input: cache
            if (useCache)
            {
                routeBuilder.Append("IDistributedCache redis, IMemoryCache memory, ");
            }

            // route: lambda input: SQL db connection string & CancellationToken
            routeBuilder.Append(@"ISqlConnectionString sql, CancellationToken cancellationToken) =>
{
    ");

            // route: lambda body: top line variable declaration
            if (useHttpGet)
            {
                routeBuilder.Append("var request = new ").Append(codeName).Append("Request(");

                for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
                {
                    var parameter = parameters![parameterIndex];
                    routeBuilder.Append("request").Append(parameter.Name);
                    if (parameterIndex < parameterCount - 1)
                    {
                        routeBuilder.Append(", ");
                    }
                }

                routeBuilder.Append(");");
            }
            else
            {
                if (parameterCount == 1)
                {
                    routeBuilder.Append("var request").Append(parameters![0].Name).Append(" = request.").Append(parameters![0].Name).Append(';');
                }
                else if (parameterCount > 1)
                {
                    routeBuilder.Append("var (");

                    for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
                    {
                        var parameter = parameters![parameterIndex];
                        routeBuilder.Append("request").Append(parameter.Name);
                        if (parameterIndex < parameterCount - 1)
                        {
                            routeBuilder.Append(", ");
                        }
                    }

                    routeBuilder.Append(") = request;");
                }
            }

            // route: lambda body: validation
            routeBuilder.Append(@"

    // Validate parameters
    var errors = default(IDictionary<string, string[]>);");

            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                var parameter = parameters![parameterIndex];

                var parameterName = parameter.Name;

                // table valued parameter
                if (parameter.TableParameters is not null)
                {
                    routeBuilder.Append(@"

    if (request").Append(parameterName).Append(@" is null)
    {");
                    if (parameter.Required)
                    {
                        routeBuilder.Append(@"
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append("\", new[] { \"").Append(parameterName).Append(" is required\" });");
                    }

                    routeBuilder.Append(@"
    }
    else
    {
        var errorList = default(HashSet<string>);

        foreach (var item in request").Append(parameterName).Append(@")
        {");
                    foreach (var item in parameter.TableParameters)
                    {
                        var itemName = item.Name;
                        var itemIsString = item.Type == "string?";
                        if (itemIsString)
                        {
                            if (item.Required)
                            {
                                routeBuilder.Append(@"

            if (string.IsNullOrWhiteSpace(item.").Append(itemName).Append(@"))
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" is required"");
            }");
                            }

                            if (item.Required && item.StringLength is not null)
                            {
                                routeBuilder.Append(@"
            else if (item.").Append(itemName).Append(".Length > ").Append(item.StringLength).Append(@")
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" must not exceed maximum length of ").Append(item.StringLength).Append(@" characters"");
            }");
                            }
                            else if (!item.Required && item.StringLength is not null)
                            {
                                routeBuilder.Append(@"

            if (!string.IsNullOrWhiteSpace(item.").Append(itemName).Append(") && item.").Append(itemName).Append(".Length > ").Append(item.StringLength).Append(@")
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" must not exceed maximum length of ").Append(item.StringLength).Append(@" characters"");
            }");
                            }
                        }
                        else
                        {
                            if (item.Required && !string.IsNullOrWhiteSpace(item.DefaultValue))
                            {
                                routeBuilder.Append(@"

            if (item.").Append(itemName).Append(@" is null)
            {
                item.").Append(itemName).Append(" = ").Append(item.DefaultValue).Append(@";
            }");
                            }
                            else if (item.Required)
                            {
                                routeBuilder.Append(@"

            if (item.").Append(itemName).Append(@" is null)
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" is required"");
            }");
                            }

                            if (item.Required && item.Positive)
                            {
                                routeBuilder.Append(@"
            else if (item.").Append(itemName).Append(@" <= 0)
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" must be greater than zero"");
            }");
                            }
                            else if (!item.Required && item.Positive)
                            {
                                routeBuilder.Append(@"

            if (item.").Append(itemName).Append(@" <= 0)
            {
                if (errorList is null) errorList = new HashSet<string>();
                errorList.Add(""").Append(itemName).Append(@" must be greater than zero"");
            }");
                            }
                        }
                    }
                    
                    routeBuilder.Append(@"

            if (errorList is not null)
            {
                if (errors is null) errors = new Dictionary<string, string[]>();
                errors.Add(""").Append(parameterName).Append(@""", errorList.ToArray());
            }
        }
    }");
                }
                else
                {
                    var paramIsString = parameter.Type == "string?";
                    if (paramIsString)
                    {
                        if (parameter.Required)
                        {
                            routeBuilder.Append(@"

    if (string.IsNullOrWhiteSpace(request").Append(parameterName).Append(@"))
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" is required""});
    }");
                        }

                        if (parameter.Required && parameter.StringLength is not null)
                        {
                            routeBuilder.Append(@"
    else if (request").Append(parameterName).Append(".Length > ").Append(parameter.StringLength).Append(@")
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" must not exceed maximum length of ").Append(parameter.StringLength).Append(@" characters""});
    }");
                        }
                        else if (!parameter.Required && parameter.StringLength is not null)
                        {
                            routeBuilder.Append(@"

    if (!string.IsNullOrWhiteSpace(request").Append(parameterName).Append(") && request").Append(parameterName).Append(".Length > ").Append(parameter.StringLength).Append(@")
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" must not exceed maximum length of ").Append(parameter.StringLength).Append(@" characters""});
    }");
                        }
                    }
                    else
                    {
                        if (parameter.Required && !string.IsNullOrWhiteSpace(parameter.DefaultValue))
                        {
                            routeBuilder.Append(@"

    if (request").Append(parameterName).Append(@" is null)
    {
        request").Append(parameterName).Append(" = ").Append(parameter.DefaultValue).Append(@";
    }");
                        }
                        else if (parameter.Required)
                        {
                            routeBuilder.Append(@"

    if (request").Append(parameterName).Append(@" is null)
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" is required""});
    }");
                        }

                        if (parameter.Required && parameter.Positive)
                        {
                            routeBuilder.Append(@"
    else if (request").Append(parameterName).Append(@" <= 0)
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" must be greater than zero""});
    }");
                        }
                        else if (!parameter.Required && parameter.Positive)
                        {
                            routeBuilder.Append(@"

    if (request").Append(parameterName).Append(@" <= 0)
    {
        if (errors is null) errors = new Dictionary<string, string[]>();
        errors.Add(""").Append(parameterName).Append(@""", new[] { """).Append(parameterName).Append(@" must be greater than zero""});
    }");
                        }
                    }
                }
            }

            routeBuilder.Append(@"

    if (errors is not null)
    {
        return Results.ValidationProblem(errors);
    }");

            // route: lambda body: cache check
            if (useCache)
            {
                routeBuilder.Append(@"

    // try retrieve from cache
    var requestJson = JsonSerializer.Serialize(request);
    var cacheKey = $""").Append(codeName).Append(@" {requestJson}"";

    if (memory.TryGetValue<List<").Append(codeName).Append(@"Response>>(cacheKey, out var cached))
    {
        Log.Information(""Memory cache {result}"", ""HIT"");
        return Results.Ok(cached);
    }

    Log.Information(""Memory cache {result}"", ""MISS"");
    var absoluteExpirationRelativeToNow = TimeSpan.From");

                if (cacheDurationSeconds is not null)
                {
                    routeBuilder.Append("Seconds(").Append(cacheDurationSeconds);
                }
                else if (cacheDurationMinutes is not null)
                {
                    routeBuilder.Append("Minutes(").Append(cacheDurationMinutes);
                }
                else
                {
                    routeBuilder.Append("Hours(").Append(cacheDurationHours);
                }

                routeBuilder.Append(@");

    var redisCached = await redis.GetStringAsync(cacheKey, cancellationToken);
    if (!string.IsNullOrWhiteSpace(redisCached))
    {
        Log.Information(""Redis cache {result}"", ""HIT"");
        var deserialized = JsonSerializer.Deserialize<List<").Append(codeName).Append(@"Response>>(redisCached);
        memory.Set(cacheKey, deserialized, absoluteExpirationRelativeToNow);
        return Results.Ok(deserialized);
    }

    Log.Information(""Redis cache {result}"", ""MISS"");");
            }

            if (returns is not null)
            {
                routeBuilder.Append(@"

    var response = new List<").Append(codeName).Append("Response>();");
            }

            // route: lambda body: procedure call
            routeBuilder.Append(@"

    // call stored procedure
    Log.Information(""Call stored procedure ").Append(procedureName).Append(@""");");

            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                var parameter = parameters![parameterIndex];

                if (parameter.TableParameters is not null)
                {
                    var parameterName = parameter.Name;

                    routeBuilder.Append(@"

    var request").Append(parameterName).Append("DataTable = new DataTable();");

                    foreach (var tvp in parameter.TableParameters)
                    {
                        routeBuilder.Append(@"
    request").Append(parameterName).Append("DataTable.Columns.Add(nameof(").Append(codeName).Append("Request").Append(parameterName).Append('.').Append(tvp.Name).Append("), typeof(").Append(tvp.Type == "string?" ? "string" : tvp.Type).Append("));");
                    }

                    routeBuilder.Append(@"
    foreach (var item in request").Append(parameterName).Append(" ?? Array.Empty<").Append(codeName).Append("Request").Append(parameterName).Append(@">())
    {
        var row = request").Append(parameterName).Append("DataTable.NewRow();");

                    foreach (var tvp in parameter.TableParameters)
                    {
                        routeBuilder.Append(@"
        row[nameof(").Append(codeName).Append("Request").Append(parameterName).Append('.').Append(tvp.Name).Append(")] = item.").Append(tvp.Name).Append(" ?? (object)DBNull.Value;");
                    }

                    routeBuilder.Append(@"
        request").Append(parameterName).Append(@"DataTable.Rows.Add(row);
    }");
                }
            }

            routeBuilder.Append(@"

    using (var connection = new SqlConnection(sql.ConnectionString))
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """).Append(procedureName).Append(@""";
        command.CommandType = CommandType.StoredProcedure;
");

            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                var parameter = parameters![parameterIndex];

                // table valued parameter
                if (parameter.TableParameters is not null)
                {
                    var parameterName = parameter.Name;

                    routeBuilder.Append(@"

        var request").Append(parameterName).Append("Param = command.Parameters.AddWithValue(\"@").Append(parameterName).Append("\", request").Append(parameterName).Append(@"DataTable);
        request").Append(parameterName).Append(@"Param.SqlDbType = SqlDbType.Structured;
        request").Append(parameterName).Append("Param.TypeName = \"").Append(parameter.Type).Append(@""";
");
                }
                else
                {
                    routeBuilder.Append(@"
        command.Parameters.AddWithValue(""@").Append(parameter.Name).Append("\", request").Append(parameter.Name).Append(");");
                }
            }

            routeBuilder.Append(@"
        await connection.OpenAsync(cancellationToken);");

            if (errors is not null)
            {
                routeBuilder.Append(@"
        try
        {");
                if (returns is null)
                {
                    routeBuilder.Append(@"
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.NoContent();");
                }
                else
                {
                    routeBuilder.Append(@"
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {");

                    foreach (var result in returns)
                    {
                        routeBuilder.Append(@"
                    var row").Append(result.Key).Append(" = default(").Append(result.Value).Append(");");
                    }

                    routeBuilder.AppendLine();

                    for (int ordinal = 0; ordinal < returns.Count; ordinal++)
                    {
                        routeBuilder.Append(@"
                    if (!reader.IsDBNull(").Append(ordinal).Append(")) row").Append(returns[ordinal].Key).Append(" = reader.Get");

                        switch (returns[ordinal].Value)
                        {
                            case "string?":
                                routeBuilder.Append("String");
                                break;
                            case "char?":
                                routeBuilder.Append("Char");
                                break;
                            case "bool?":
                                routeBuilder.Append("Boolean");
                                break;
                            case "int?":
                                routeBuilder.Append("Int32");
                                break;
                            case "short?":
                                routeBuilder.Append("Int16");
                                break;
                            case "long?":
                                routeBuilder.Append("Int64");
                                break;
                            case "DateTime?":
                                routeBuilder.Append("DateTime");
                                break;
                            case "DateTimeOffset?":
                                routeBuilder.Append("DateTimeOffset");
                                break;
                            case "decimal?":
                                routeBuilder.Append("Decimal");
                                break;
                            case "double?":
                                routeBuilder.Append("Double");
                                break;
                            case "Guid?":
                                routeBuilder.Append("Guid");
                                break;
                            default:
                                break;
                        }

                        routeBuilder.Append('(').Append(ordinal).Append(");");
                    }

                    routeBuilder.Append(@"

                    var result = new ").Append(codeName).Append("Response(");

                    for (int i = 0; i < returns.Count; i++)
                    {
                        routeBuilder.Append("row").Append(returns[i].Key);
                        if (i < returns.Count - 1)
                        {
                            routeBuilder.Append(", ");
                        }
                    }

                    routeBuilder.Append(@");

                    response.Add(result);
                }
            }");
                }

                routeBuilder.Append(@"
        }
        catch (SqlException error)
        {
            switch (error.Message)
            {");

                foreach (var error in errors)
                {
                    routeBuilder.Append(@"
                case """).Append(error.Value).Append(@""":
                    Log.Warning(""Received error from stored procedure {procedure} {error}"", """).Append(procedureName).Append("\", \"").Append(error.Value).Append(@""");
                    return Results.Problem(""").Append(error.Value).Append("\", statusCode: ").Append(error.Key).Append(");");
                }

                routeBuilder.Append(@"
                default:
                    throw error;
            }
        }");
            }
            else
            {
                if (returns is null)
                {
                    routeBuilder.Append(@"
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Results.NoContent();");
                }
                else
                {
                    routeBuilder.Append(@"
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {");

                    foreach (var result in returns)
                    {
                        routeBuilder.Append(@"
                var row").Append(result.Key).Append(" = default(").Append(result.Value).Append(");");
                    }

                    routeBuilder.AppendLine();

                    for (int ordinal = 0; ordinal < returns.Count; ordinal++)
                    {
                        routeBuilder.Append(@"
                if (!reader.IsDBNull(").Append(ordinal).Append(")) row").Append(returns[ordinal].Key).Append(" = reader.Get");

                        switch (returns[ordinal].Value)
                        {
                            case "string?":
                                routeBuilder.Append("String");
                                break;
                            case "char?":
                                routeBuilder.Append("Char");
                                break;
                            case "bool?":
                                routeBuilder.Append("Boolean");
                                break;
                            case "int?":
                                routeBuilder.Append("Int32");
                                break;
                            case "short?":
                                routeBuilder.Append("Int16");
                                break;
                            case "long?":
                                routeBuilder.Append("Int64");
                                break;
                            case "DateTime?":
                                routeBuilder.Append("DateTime");
                                break;
                            case "DateTimeOffset?":
                                routeBuilder.Append("DateTimeOffset");
                                break;
                            case "decimal?":
                                routeBuilder.Append("Decimal");
                                break;
                            case "double?":
                                routeBuilder.Append("Double");
                                break;
                            case "Guid?":
                                routeBuilder.Append("Guid");
                                break;
                            default:
                                break;
                        }

                        routeBuilder.Append('(').Append(ordinal).Append(");");
                    }

                    routeBuilder.Append(@"

                var result = new ").Append(codeName).Append("Response(");

                    for (int i = 0; i < returns.Count; i++)
                    {
                        routeBuilder.Append("row").Append(returns[i].Key);
                        if (i < returns.Count - 1)
                        {
                            routeBuilder.Append(", ");
                        }
                    }

                    routeBuilder.Append(@");

                response.Add(result);
            }
        }");
                }
            }

            routeBuilder.Append(@"
    }");


            // route: lambda body: cache result
            if (useCache)
            {
                routeBuilder.Append(@"

    memory.Set(cacheKey, response, absoluteExpirationRelativeToNow);
    var serialized = JsonSerializer.Serialize(response);
    await redis.SetStringAsync(cacheKey, serialized, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow }, cancellationToken);");
            }

            // route: lambda body: return result
            if (returns is not null)
            {
                routeBuilder.Append(@"

    return Results.Ok(response);");
            }

            // route: route builder fluent API calls
            routeBuilder.Append(@"
})
.RequireAuthorization()
.WithName(""").Append(procedureName).Append(@""")
.Produces(").Append(returns is not null ? $"200, typeof(List<{codeName}Response>)" : "204").Append(@")
.ProducesValidationProblem()
.Produces(401)");

            if (errors is not null)
            {
                foreach (var item in errors.Select(e => e.Key).Distinct())
                {
                    routeBuilder.Append(@"
.Produces(").Append(item).Append(')');
                }
            }

            routeBuilder.Append(';');

            routes.Add(routeBuilder.ToString());

            // generate code for records

            // request records
            var requestBuilder = new StringBuilder(@"record struct ").Append(codeName).Append("Request(");

            if (parameters is not null) 
            {
                for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
                {
                    var parameter = parameters[parameterIndex];

                    if (parameter.TableParameters is not null)
                    {
                        var recordName = $"{codeName}Request{parameter.Name}";

                        var tvpBuilder = new StringBuilder(@"record struct ").Append(recordName).Append('(');

                        var tvpCount = parameter.TableParameters.Count();
                        for (var tvpIndex = 0; tvpIndex < tvpCount; tvpIndex++)
                        {
                            var tvpField = parameter.TableParameters.ElementAt(tvpIndex);
                            tvpBuilder.Append(tvpField.Type).Append(' ').Append(tvpField.Name);
                            if (tvpIndex < tvpCount - 1)
                            {
                                tvpBuilder.Append(", ");
                            }
                        }

                        tvpBuilder.Append(");");

                        records.Add(new KeyValuePair<string, string>(recordName, tvpBuilder.ToString()));

                        requestBuilder.Append("IEnumerable<").Append(codeName).Append("Request").Append(parameter.Name).Append("> ").Append(parameter.Name);
                    }
                    else
                    {
                        requestBuilder.Append(parameter.Type).Append(' ').Append(parameter.Name);
                    }

                    if (parameterIndex < parameterCount - 1)
                    {
                        requestBuilder.Append(", ");
                    }
                }
            }

            requestBuilder.Append(");");

            records.Add(new KeyValuePair<string, string>($"{codeName}Request", requestBuilder.ToString()));

            if (returns is not null) // response records 
            {

                var responseBuilder = new StringBuilder(@"record struct ").Append(codeName).Append("Response(");

                for (int i = 0; i < returns.Count; i++)
                {
                    responseBuilder.Append(returns[i].Value).Append(' ').Append(returns[i].Key);

                    if (i < returns.Count - 1)
                    {
                        responseBuilder.Append(", ");
                    }
                }

                responseBuilder.Append(");");

                records.Add(new KeyValuePair<string, string>($"{codeName}Response", responseBuilder.ToString()));
            }
        });

        var sb = new StringBuilder(@"// <auto-generated />
using GeneratedSource;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Data;
using System.Text.Json;

#nullable enable

var app = WebApplication.CreateBuilder(args).Setup();
");

        foreach (var route in routes.OrderBy(abc => abc)) sb.AppendLine(route);

        sb.AppendLine(@"
app.Run();
");

        context.AddSource("Program.cs", sb.ToString());

        foreach (var record in records)
        {
            context.AddSource($"{record.Key}.cs", @$"// <auto-generated />
namespace GeneratedSource;

#nullable enable

internal {record.Value}
");
        }

        context.AddSource("ISqlConnectionString.cs", @"// <auto-generated />
namespace GeneratedSource;

#nullable enable

internal interface ISqlConnectionString
{
    string ConnectionString { get; }
}
");

        context.AddSource("SqlConnectionString.cs", @"// <auto-generated />
namespace GeneratedSource;

#nullable enable

internal class SqlConnectionString : ISqlConnectionString
{
    public string ConnectionString { get; init; } = default!;
}
");

        context.AddSource("WebApplicationBuilderExtensions.cs", @"// <auto-generated />
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

namespace GeneratedSource;

#nullable enable

static class WebApplicationBuilderExtensions
{
    public static WebApplication Setup(this WebApplicationBuilder builder)
    {
        // remove 'server: Kestrel' header
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        // use Serilog logger
        Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();

        builder.WebHost.UseSerilog();

        // add health checks
        builder.Services.AddHealthChecks()
            .AddSqlServer(builder.Configuration.GetConnectionString(""DefaultConnection""));

        // add swagger UI
        builder.Services
            .AddEndpointsApiExplorer()
            .AddSwaggerGen(setup =>
            {
                setup.AddSecurityDefinition(""JWT Bearer"", new OpenApiSecurityScheme
                {
                    Name = ""Authorization"",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = ""Bearer"",
                    BearerFormat = ""JWT"",
                    In = ParameterLocation.Header,
                    Description = @""JWT Authorization header using the Bearer scheme.

Enter 'Bearer' [space] and then your token in the text input below.

Example: """"Bearer qwerty.asdfgh.zxcvbn\"""""",
                });
                setup.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = ""Bearer""
                            }
                        },
                        Array.Empty<string>()
                     }
                 });
            });

        // add SQL Server database connection string
        builder.Services.AddSingleton<ISqlConnectionString>(new SqlConnectionString 
        { 
            ConnectionString = builder.Configuration.GetConnectionString(""DefaultConnection"") 
        });

        // add memory cache & redis cache
        builder.Services
            .AddMemoryCache()
            .AddStackExchangeRedisCache(setup =>
            {
                setup.InstanceName = builder.Configuration[""Redis:InstanceName""];
            });

        // add JWT bearer auth
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration[""Auth:Authority""];
            options.Audience = builder.Configuration[""Auth:Audience""];
        });

        // require authentication user
        builder.Services.AddAuthorization(configure =>
        {
            configure.AddPolicy(""Default"", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AuthenticationSchemes = new List<string> { JwtBearerDefaults.AuthenticationScheme };
            });
        });

        var app = builder.Build();

        app.UseSerilogRequestLogging();

        app.MapSwagger(""/{documentName}/swagger.{json|yaml}"");
        app.UseSwaggerUI(setup =>
        {
            setup.RoutePrefix = """";
        });

        app.MapHealthChecks(""/health"");

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
");
    }

    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        //if (!Debugger.IsAttached)
        //{
        //    Debugger.Launch();
        //}
#endif
    }
}

record struct ProcedureParameter(string Name, string Type, int? StringLength = default, bool Required = false, bool Positive = false, string? DefaultValue = default, IEnumerable<ProcedureParameter>? TableParameters = default);

static class StringExtensions
{
    public static string ToSystemType(this string type)
    {
        var typeUpper = type.ToUpper();
        if (typeUpper.Contains("VARCHAR")) return "string";
        return typeUpper switch
        {
            "DATETIMEOFFSET" => "DateTimeOffset",
            "DATETIME" => "DateTime",
            "DATE" => "DateTime",
            "BIGINT" => "long",
            "INT" => "int",
            "SMALLINT" => "short",
            "SHORT" => "short",
            "TINYINT" => "byte",
            "BIT" => "byte",
            "BYTE" => "byte",
            "DECIMAL" => "decimal",
            "DOUBLE" => "double",
            "GUID" => "Guid",
            "CHAR" => "char",
            "BOOL" => "bool",
            "BOOLEAN" => "bool",
            _ => type,
        };
    }

    public static string ToSystemTypeNullable(this string type)
        => type.ToSystemType() + '?';
}
