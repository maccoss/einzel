using System.Text.Json.Nodes;
using Einzel.Core.Errors;

namespace Einzel.Extensions;

/// <summary>
/// Checks an extension's output against the schema its manifest declares.
/// </summary>
/// <remarks>
/// <para>
/// EXT-7. Without it an extension that returns the wrong shape produces a null or a
/// zero somewhere downstream, and the traceback points at the engine rather than at
/// the extension - which is exactly the debugging session that makes people stop
/// writing extensions.
/// </para>
/// <para>
/// A deliberate subset of JSON Schema, not an implementation of it: type, required,
/// properties, items, enum, and numeric bounds. That covers what an extension
/// contract actually says, and the alternative - a dependency implementing the
/// whole specification including remote <c>$ref</c> resolution - would put a
/// network fetch inside a sandbox whose entire purpose is not having one.
/// </para>
/// <para>
/// Anything unrecognised is <em>ignored rather than refused</em>, so a manifest
/// carrying a richer schema for a human reader still validates on the parts this
/// understands. What is not allowed is silently passing something the schema
/// forbids in a keyword that is supported.
/// </para>
/// </remarks>
public static class SchemaCheck
{
    /// <summary>Validates a document, throwing if it does not conform.</summary>
    /// <param name="value">The document.</param>
    /// <param name="schema">The schema, or null to accept anything.</param>
    /// <param name="who">The extension name, for the error message.</param>
    /// <exception cref="EinzelException">The document does not conform.</exception>
    public static void Validate(JsonNode? value, JsonNode? schema, string who)
    {
        if (schema is null)
        {
            return;
        }

        var failure = Check(value, schema, "/");

        if (failure is null)
        {
            return;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = failure.Value.Path,
            Constraint = $"'{who}' returned a document its own manifest forbids: {failure.Value.Why}",
            Suggestion = "either the extension is wrong or its declared outputSchema is. Run "
                + "'einzel ext test' to see the document it produced beside the schema it "
                + "declared",
        });
    }

    /// <summary>Whether a document conforms, without throwing.</summary>
    /// <param name="value">The document.</param>
    /// <param name="schema">The schema, or null to accept anything.</param>
    /// <returns>Why it does not conform, or null when it does.</returns>
    public static string? Explain(JsonNode? value, JsonNode? schema) =>
        schema is null ? null : Check(value, schema, "/")?.Why;

    private static (string Path, string Why)? Check(JsonNode? value, JsonNode schema, string path)
    {
        if (schema is not JsonObject rules)
        {
            return null;
        }

        if (rules["type"]?.GetValue<string>() is { } expected)
        {
            var actual = TypeOf(value);

            // Integer is a number that happens to be whole, which is what every JSON
            // producer means by it and what a Python extension returning 3 produces.
            var ok = expected switch
            {
                "integer" => actual == "integer",
                "number" => actual is "number" or "integer",
                _ => actual == expected,
            };

            if (!ok)
            {
                return (path, $"expected {expected} at {path}, got {actual}");
            }
        }

        if (rules["enum"] is JsonArray allowed
            && !allowed.Any(a => JsonNode.DeepEquals(a, value)))
        {
            return (path, $"{path} is not one of the declared values");
        }

        if (value is JsonValue number && number.TryGetValue<double>(out var scalar))
        {
            if (rules["minimum"]?.GetValue<double>() is { } least && scalar < least)
            {
                return (path, $"{path} is {scalar:G6}, below the declared minimum of {least:G6}");
            }

            if (rules["maximum"]?.GetValue<double>() is { } most && scalar > most)
            {
                return (path, $"{path} is {scalar:G6}, above the declared maximum of {most:G6}");
            }
        }

        if (value is JsonObject document)
        {
            if (rules["required"] is JsonArray required)
            {
                foreach (var name in required)
                {
                    var key = name?.GetValue<string>();

                    if (key is not null && !document.ContainsKey(key))
                    {
                        return (path, $"{path} is missing the required property '{key}'");
                    }
                }
            }

            if (rules["properties"] is JsonObject properties)
            {
                foreach (var (key, sub) in properties)
                {
                    if (sub is null || !document.TryGetPropertyValue(key, out var actual))
                    {
                        continue;
                    }

                    var failure = Check(actual, sub, path == "/" ? $"/{key}" : $"{path}/{key}");

                    if (failure is not null)
                    {
                        return failure;
                    }
                }
            }
        }

        if (value is JsonArray items && rules["items"] is { } itemSchema)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var failure = Check(items[i], itemSchema, $"{(path == "/" ? string.Empty : path)}/{i}");

                if (failure is not null)
                {
                    return failure;
                }
            }
        }

        return null;
    }

    private static string TypeOf(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue scalar when scalar.TryGetValue<bool>(out _) => "boolean",
        JsonValue scalar when scalar.TryGetValue<string>(out _) => "string",
        JsonValue scalar when scalar.TryGetValue<long>(out _) => "integer",
        JsonValue scalar when scalar.TryGetValue<double>(out _) => "number",
        _ => "unknown",
    };
}
