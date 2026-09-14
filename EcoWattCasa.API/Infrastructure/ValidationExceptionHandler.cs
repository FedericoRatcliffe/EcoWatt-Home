using EcoWattCasa.Application.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Infrastructure;

/// <summary>
/// Traduce los errores de regla de negocio a 400 con ProblemDetails, para no repetir
/// try/catch en cada controlador.
/// </summary>
public sealed class ValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DeviceValidationException)
            return false;

        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Title = "Pedido invalido",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            }
        });
    }
}
