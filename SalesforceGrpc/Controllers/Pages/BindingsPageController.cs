using Application.Pipeline;
using Application.Services.Interfaces;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

/// <summary>
/// The Bindings pages: the list of the Primary Channel's Entities, choosing a Target Table for one, and one
/// Binding's editor. See ADR 0006.
/// </summary>
/// <remarks>
/// The list reads the App Database only, so it works while the Target Database is down. The other two read the
/// Target Database and say so in place of what they could not load. Every page waits, showing only what is
/// missing, until there is a Primary Channel and a usable Target Connection.
/// </remarks>
public class BindingsPageController(IPipelineStatusService pipeline, IBindingService bindings,
    IPlatformEventChannelRepository channels) : PageController(pipeline) {

    [HttpGet("/bindings")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var status = await ReadPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (BindingsSetup.Missing(status) is { Count: > 0 } missing) {
            return RenderPage("bindings", "Bindings", NavKey.Bindings, status, BindingsView.WaitingOn(missing));
        }

        var primary = await channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        var all = await bindings.GetBindingsAsync(cancellationToken).ConfigureAwait(false);

        return RenderPage("bindings", "Bindings", NavKey.Bindings, status, BindingsView.For(primary, all));
    }

    [HttpGet("/bindings/new")]
    public async Task<IActionResult> New([FromQuery] int? member, CancellationToken cancellationToken) {
        const string title = "New Binding";
        var status = await ReadPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (BindingsSetup.Missing(status) is { Count: > 0 } missing) {
            return RenderPage("binding-new", title, NavKey.Bindings, status, NewBindingView.WaitingOn(missing));
        }

        var primary = await channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        var chosen = member is { } memberId
            ? await channels.GetMemberByIdAsync(memberId, cancellationToken).ConfigureAwait(false)
            : null;
        if (chosen is null || primary is null || chosen.ChannelId != primary.Id) {
            return RenderPage("binding-new", title, NavKey.Bindings, status, NewBindingView.NotFound());
        }

        // An Entity has one destination; one already bound is edited, not bound again.
        var all = await bindings.GetBindingsAsync(cancellationToken).ConfigureAwait(false);
        if (BindingsView.BindingsByEntity(all).TryGetValue(chosen.SelectedEntity, out var existing)) {
            return Redirect($"/bindings/{existing.Id}");
        }

        NewBindingView view;
        try {
            view = NewBindingView.For(chosen,
                await bindings.GetTargetTablesAsync(null, cancellationToken).ConfigureAwait(false));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            view = NewBindingView.Unreadable(chosen, BindingsSetup.LoadError(ex));
        }

        return RenderPage("binding-new", $"Bind {chosen.SelectedEntity}", NavKey.Bindings, status, view);
    }

    [HttpGet("/bindings/{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken cancellationToken) {
        var status = await ReadPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (BindingsSetup.Missing(status) is { Count: > 0 } missing) {
            return RenderPage("binding", "Binding", NavKey.Bindings, status, BindingView.WaitingOn(missing));
        }

        BindingDTO binding;
        try {
            binding = await bindings.GetBindingAsync(id, cancellationToken).ConfigureAwait(false);
        } catch (KeyNotFoundException) {
            return RenderPage("binding", "Binding not found", NavKey.Bindings, status, BindingView.NotFound());
        }

        var primary = await channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        var memberId = primary?.Members
            .FirstOrDefault(m => string.Equals(m.SelectedEntity, binding.EntityName, StringComparison.OrdinalIgnoreCase))?.Id;

        BindingTargetView? target = null;
        TargetDatabaseErrorView? targetError = null;
        try {
            target = BindingTargetView.For(binding,
                await bindings.GetBindableFieldsForBindingAsync(id, cancellationToken).ConfigureAwait(false),
                await bindings.GetBindingColumnsAsync(id, cancellationToken).ConfigureAwait(false),
                await bindings.GetSoftDeleteColumnsAsync(id, cancellationToken).ConfigureAwait(false),
                await bindings.ValidateBindingAsync(id, cancellationToken).ConfigureAwait(false));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            targetError = BindingsSetup.LoadError(ex);
        }

        return RenderPage("binding", $"{binding.EntityName} → {binding.TargetTable}", NavKey.Bindings, status,
            new BindingView([], BindingEditorView.For(binding, memberId, target, targetError)));
    }
}
