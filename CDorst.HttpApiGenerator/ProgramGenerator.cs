using CDorst.HttpApiGenerator.Builders;
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
            string? procedureName, codeName;
            int? cacheDurationSeconds, cacheDurationMinutes, cacheDurationHours;
            bool useCache, useHttpGet;
            List<ProcedureParameter>? parameters;
            List<KeyValuePair<string, string>>? returns;
            List<KeyValuePair<int, string>>? errors;
            SqlProcedureRequirementsParser.ParseProcedureRequirements(sqlCodeBlock, out procedureName, out codeName, out cacheDurationSeconds, out cacheDurationMinutes, out cacheDurationHours, out useCache, out useHttpGet, out parameters, out returns, out errors);

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

            routeBuilder.AppendRouteValidation(parameters, parameterCount);

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

                        routeBuilder.Append(returns[ordinal].Value.ToSqlDataReaderType());

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

                        routeBuilder.Append(returns[ordinal].Value.ToSqlDataReaderType());

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
.ProducesProblem(").Append(item).Append(')');
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

        context.AddConstantTypesSource()
            .AddRecordSource(records)
            .AddProgramSource(routes);
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
