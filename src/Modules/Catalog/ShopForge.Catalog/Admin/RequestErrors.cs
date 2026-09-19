using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ShopForge.Catalog.Admin;

internal sealed class RequestErrors
{
    private readonly Dictionary<string, string[]> _errors = new(StringComparer.OrdinalIgnoreCase);

    public bool Any => _errors.Count > 0;

    public RequestErrors Check(bool isValid, string field, string message)
    {
        if (!isValid && !_errors.ContainsKey(field))
        {
            _errors[field] = [message];
        }

        return this;
    }

    public ValidationProblem ToProblem() => TypedResults.ValidationProblem(_errors);
}
