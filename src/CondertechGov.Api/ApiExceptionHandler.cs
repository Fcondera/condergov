using CondertechGov.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CondertechGov.Api;
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception e, CancellationToken ct)
    {
        var (status, title) = e switch
        {
            ValidationException => (400, "Dados inválidos."),
            DomainException => (400, e.Message),
            ConflictException => (409, e.Message),
            PublicDataNotFoundException => (404, "Dado não encontrado no provedor externo."),
            PublicApiException { Reason: "timeout" } => (504, "O provedor externo não respondeu dentro do prazo."),
            PublicApiException { Reason: "disabled" or "busy" or "rate_limited" } => (503, "Consulta externa indisponível no momento."),
            PublicApiException => (502, "Não foi possível obter uma resposta válida do provedor externo."),
            BadHttpRequestException => (400, "Requisição inválida."),
            UnauthorizedAccessException => (403, "Acesso negado."),
            DbUpdateConcurrencyException => (409, "O registro foi alterado. Recarregue e tente novamente."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => (409, "Registro ou chave já utilizado."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23503" } } => (400, "Relacionamento indisponível."),
            DbUpdateException { InnerException: PostgresException { SqlState: "40001" } } => (409, "Operação concorrente. Tente novamente."),
            PostgresException { SqlState: "40001" } => (409, "Operação concorrente. Tente novamente."),
            _ => (500, "Não foi possível concluir a operação.")
        };
        if (status == 500) logger.LogError(e, "Erro interno. Correlação {Correlation}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = title };
        if (e is PublicApiException external) { problem.Extensions["provider"] = external.Provider.ToString(); problem.Extensions["reason"] = external.Reason; }
        if (e is ValidationException validation) problem.Extensions["errors"] = validation.Errors.GroupBy(x => x.PropertyName).ToDictionary(x => x.Key, x => x.Select(v => v.ErrorMessage).ToArray());
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
