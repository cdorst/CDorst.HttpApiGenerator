﻿using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CDorst.HttpApiGenerator.Builders;

internal static class ProgramBuilder
{
    public static GeneratorExecutionContext AddProgramSource(this GeneratorExecutionContext context, IEnumerable<string> routes)
    {
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

        return context;
    }
}
