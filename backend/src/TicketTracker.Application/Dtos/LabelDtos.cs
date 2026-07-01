namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5.6/§5.7 (Wave 2, ADR-0016). Labels are team-scoped, member-managed.

/// <summary>A label as returned by the label CRUD endpoints (GET/POST/PUT /api/labels).</summary>
public sealed record LabelDto(Guid Id, Guid TeamId, string Name, string Color);

/// <summary>
/// A lightweight label reference (id + name + color) carried on ticket detail/card (§8.5). Color is
/// needed to render the chip; the SPA never recomputes it.
/// </summary>
public sealed record LabelRefDto(Guid Id, string Name, string Color);

/// <summary>Create a label in a team (POST /api/labels). All fields validated in the service.</summary>
public sealed record CreateLabelRequest(Guid? TeamId, string? Name, string? Color);

/// <summary>Rename / recolor a label (PUT /api/labels/{id}). Team is immutable (labels never move teams).</summary>
public sealed record UpdateLabelRequest(string? Name, string? Color);

/// <summary>
/// Full-set replace of a ticket's labels (PUT /api/tickets/{id}/labels). Authoritative complete set;
/// null/omitted ⇒ clear all (mirrors <see cref="SetAssigneesRequest"/>, §5.7).
/// </summary>
public sealed record SetLabelsRequest(IReadOnlyList<Guid>? LabelIds);
