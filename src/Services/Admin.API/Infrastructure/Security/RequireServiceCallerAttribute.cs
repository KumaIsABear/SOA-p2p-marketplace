using Microsoft.AspNetCore.Mvc;

namespace Admin.API.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireServiceCallerAttribute : TypeFilterAttribute {
    public RequireServiceCallerAttribute(params string[] allowedCallers)
        : base(typeof(ServiceKeyAuthorizationFilter)) {
        Arguments = [allowedCallers];
    }
}
