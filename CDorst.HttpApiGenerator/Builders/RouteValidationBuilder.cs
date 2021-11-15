using System.Collections.Generic;
using System.Text;

namespace CDorst.HttpApiGenerator.Builders;

internal static class RouteValidationBuilder
{
    public static StringBuilder AppendRouteValidation(this StringBuilder routeBuilder, List<ProcedureParameter>? parameters, int parameterCount)
    {
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

        return routeBuilder.Append(@"

    if (errors is not null)
    {
        return Results.ValidationProblem(errors);
    }");
    }
}
