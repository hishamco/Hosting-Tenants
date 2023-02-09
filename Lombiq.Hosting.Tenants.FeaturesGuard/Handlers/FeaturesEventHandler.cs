using Lombiq.Hosting.Tenants.FeaturesGuard.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Extensions.Features;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Descriptor;
using OrchardCore.Environment.Shell.Descriptor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lombiq.Hosting.Tenants.FeaturesGuard.Handlers;

public sealed class FeaturesEventHandler : IFeatureEventHandler
{
    private readonly IShellFeaturesManager _shellFeaturesManager;
    private readonly IOptions<ConditionallyEnabledFeaturesOptions> _conditionallyEnabledFeaturesOptions;
    private readonly ShellSettings _shellSettings;
    private readonly IShellDescriptorManager _shellDescriptorManager;

    public FeaturesEventHandler(
        IShellFeaturesManager shellFeaturesManager,
        IOptions<ConditionallyEnabledFeaturesOptions> conditionallyEnabledFeaturesOptions,
        ShellSettings shellSettings,
        IConfiguration configuration,
        IShellDescriptorManager shellDescriptorManager)
    {
        _shellFeaturesManager = shellFeaturesManager;
        _conditionallyEnabledFeaturesOptions = conditionallyEnabledFeaturesOptions;
        _shellSettings = shellSettings;
        _shellDescriptorManager = shellDescriptorManager;
    }

    Task IFeatureEventHandler.InstallingAsync(IFeatureInfo feature) => Task.CompletedTask;

    Task IFeatureEventHandler.InstalledAsync(IFeatureInfo feature) => Task.CompletedTask;

    Task IFeatureEventHandler.EnablingAsync(IFeatureInfo feature) => Task.CompletedTask;

    Task IFeatureEventHandler.EnabledAsync(IFeatureInfo feature) => EnableConditionallyEnabledFeaturesAsync(feature);

    Task IFeatureEventHandler.DisablingAsync(IFeatureInfo feature) => Task.CompletedTask;

    async Task IFeatureEventHandler.DisabledAsync(IFeatureInfo feature)
    {
        await KeepConditionallyEnabledFeaturesEnabledAsync(feature);
        await DisableConditionallyEnabledFeaturesAsync(feature);
    }

    Task IFeatureEventHandler.UninstallingAsync(IFeatureInfo feature) => Task.CompletedTask; // #spell-check-ignore-line

    Task IFeatureEventHandler.UninstalledAsync(IFeatureInfo feature) => Task.CompletedTask;

    /// <summary>
    /// Enables conditional features (key) if one of their corresponding condition features (value) was enabled.
    /// </summary>
    /// <param name="featureInfo">The feature that was just enabled.</param>
    public async Task EnableConditionallyEnabledFeaturesAsync(IFeatureInfo featureInfo)
    {
        if (_shellSettings.IsDefaultShell() ||
            _conditionallyEnabledFeaturesOptions.Value.EnableFeatureIfOtherFeatureIsEnabled is not { } conditionallyEnabledFeatures)
        {
            return;
        }

        var allConditionFeatureIds = new List<string>();
        foreach (var conditionFeatureIds in conditionallyEnabledFeatures.Values)
        {
            allConditionFeatureIds.AddRange(conditionFeatureIds);
        }

        if (!allConditionFeatureIds.Contains(featureInfo.Id))
        {
            return;
        }

        // Enable conditional features if they are not already enabled.
        var allFeatures = await _shellFeaturesManager.GetAvailableFeaturesAsync();

        var conditionalFeatureIds = conditionallyEnabledFeatures
            .Where(keyValuePair => keyValuePair.Value.Contains(featureInfo.Id))
            .Select(keyValuePair => keyValuePair.Key)
            .ToList();

        // During setup, Shell Descriptor can become out of sync with the DB when it comes to enabled features,
        // but it's more accurate than IShellDescriptorManager's methods.
        var shellDescriptor = await _shellDescriptorManager.GetShellDescriptorAsync();

        // If Shell Descriptor's Features already contains a feature that is found in conditionalFeatures, remove it
        // from the list. Handle multiple conditional features as well.
        var featuresToEnable = allFeatures.Where(feature =>
            conditionalFeatureIds.Contains(feature.Id) && !shellDescriptor.Features.Contains(new ShellFeature(feature.Id)));

        await _shellFeaturesManager.EnableFeaturesAsync(featuresToEnable, force: true);
    }

    /// <summary>
    /// When a conditional feature (key) is disabled, keeps the conditional feature enabled if any of the corresponding
    /// condition features (value) are enabled.
    /// </summary>
    /// <param name="featureInfo">The feature that was just disabled.</param>
    public async Task KeepConditionallyEnabledFeaturesEnabledAsync(IFeatureInfo featureInfo)
    {
        if (_shellSettings.IsDefaultShell() ||
            _conditionallyEnabledFeaturesOptions.Value.EnableFeatureIfOtherFeatureIsEnabled is not { } conditionallyEnabledFeatures)
        {
            return;
        }

        if (!conditionallyEnabledFeatures.ContainsKey(featureInfo.Id))
        {
            return;
        }

        // Re-enable conditional feature if any its condition features are enabled.
        var allFeatures = await _shellFeaturesManager.GetAvailableFeaturesAsync();
        var conditionFeatureIds = conditionallyEnabledFeatures[featureInfo.Id];

        var currentlyEnabledFeatures = await _shellFeaturesManager.GetEnabledFeaturesAsync();
        var conditionFeatures = allFeatures.Where(feature => conditionFeatureIds.Contains(feature.Id));

        var currentlyEnabledConditionFeatures = currentlyEnabledFeatures.Intersect(conditionFeatures);
        if (currentlyEnabledConditionFeatures.Any())
        {
            var conditionalFeature = allFeatures.Where(feature => feature.Id == featureInfo.Id);
            await _shellFeaturesManager.EnableFeaturesAsync(conditionalFeature);
        }
    }

    /// <summary>
    /// When a condition feature (value) is disabled, disables the corresponding conditional features (key) if all of
    /// their condition features are disabled.
    /// </summary>
    /// <param name="featureInfo">The feature that was just disabled.</param>
    public async Task DisableConditionallyEnabledFeaturesAsync(IFeatureInfo featureInfo)
    {
        if (_shellSettings.IsDefaultShell() ||
            _conditionallyEnabledFeaturesOptions.Value.EnableFeatureIfOtherFeatureIsEnabled is not { } conditionallyEnabledFeatures)
        {
            return;
        }

        var allConditionFeatureIds = new List<string>();
        foreach (var conditionFeatureIdList in conditionallyEnabledFeatures.Values)
        {
            allConditionFeatureIds.AddRange(conditionFeatureIdList);
        }

        if (!allConditionFeatureIds.Contains(featureInfo.Id))
        {
            return;
        }

        // If current feature is one of the condition features, disable its corresponding conditional features if they
        // are not already disabled.
        var allFeatures = await _shellFeaturesManager.GetAvailableFeaturesAsync();

        var conditionalFeatureIds = new List<string>();
        var conditionFeatureIds = new List<string>();
        foreach (var keyValuePair in conditionallyEnabledFeatures.Where(keyValuePair => keyValuePair.Value.Contains(featureInfo.Id)))
        {
            conditionalFeatureIds.Add(keyValuePair.Key);
            conditionFeatureIds.AddRange(keyValuePair.Value);
        }

        var currentlyEnabledFeatures = await _shellFeaturesManager.GetEnabledFeaturesAsync();
        var conditionFeatures = allFeatures.Where(feature => conditionFeatureIds.Contains(feature.Id));

        // Only disable conditional feature if none of its condition features are enabled.
        var currentlyEnabledConditionFeatures = currentlyEnabledFeatures.Intersect(conditionFeatures);
        if (!currentlyEnabledConditionFeatures.Any())
        {
            // Handle multiple conditional features as well.
            var conditionalFeatures = allFeatures.Where(feature => conditionalFeatureIds.Contains(feature.Id));
            var currentlyEnabledConditionalFeatures = currentlyEnabledFeatures.Intersect(conditionalFeatures);

            await _shellFeaturesManager.DisableFeaturesAsync(currentlyEnabledConditionalFeatures);
        }
    }
}
