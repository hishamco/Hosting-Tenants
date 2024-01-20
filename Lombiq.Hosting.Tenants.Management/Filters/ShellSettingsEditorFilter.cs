using Lombiq.Hosting.Tenants.Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrchardCore.DisplayManagement;
using OrchardCore.DisplayManagement.Layout;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.Mvc.Core.Utilities;
using OrchardCore.Tenants.Controllers;
using System.Threading.Tasks;

namespace Lombiq.Hosting.Tenants.Management.Filters;

public class ShellSettingsEditorFilter(
    ILayoutAccessor layoutAccessor,
    IShapeFactory shapeFactory,
    IShellHost shellHost) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var actionRouteController = context.ActionDescriptor.RouteValues["Controller"];
        var actionRouteArea = context.ActionDescriptor.RouteValues["Area"];
        var actionRouteValue = context.ActionDescriptor.RouteValues["Action"];

        if (actionRouteController == typeof(AdminController).ControllerName() &&
            actionRouteArea == $"{nameof(OrchardCore)}.{nameof(OrchardCore.Tenants)}" &&
            actionRouteValue is nameof(AdminController.Edit) &&
            context.Result is ViewResult)
        {
            var tenantName = context.RouteData.Values["Id"].ToString();
            if (!shellHost.TryGetSettings(tenantName, out var shellSettings))
            {
                await next();
                return;
            }

            var layout = await layoutAccessor.GetLayoutAsync();
            var contentZone = layout.Zones["Content"];

            (context.Controller as Controller)
                !.TempData
                .TryGetValue(
                    "ValidationErrorJson",
                    out var validationErrorJson);

            var editableItems = shellSettings.ShellConfiguration.AsJsonNode();
            var editorJson = string.IsNullOrEmpty(validationErrorJson?.ToString())
                ? editableItems[$"{tenantName}Prefix"]?.ToJsonString()
                : validationErrorJson.ToString();

            await contentZone.AddAsync(
                await shapeFactory.CreateAsync<ShellSettingsEditorViewModel>(
                    "ShellSettingsEditor",
                    viewModel =>
                    {
                        viewModel.Json = editorJson;
                        viewModel.TenantId = tenantName;
                    }),
                "10");
        }

        await next();
    }
}
