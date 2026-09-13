namespace Waymark.Application.Commands;

/// <summary>
/// A request to change something, and what it answers with.
///
/// <para>
/// Deliberately empty. It exists so a command is a type the executor can be
/// generic over, not so commands share behaviour — a base class with behaviour
/// is how validation, logging and authorisation end up somewhere nobody looks
/// for them.
/// </para>
/// <para>
/// A command that answers nothing still has a result. Use
/// <see cref="NoResult"/> rather than a second non-generic interface: two
/// interfaces means two handler shapes, two executor overloads and two places
/// for the <c>processing_log</c> rule to be forgotten.
/// </para>
/// </summary>
/// <typeparam name="TResult">What the caller gets back.</typeparam>
public interface ICommand<out TResult>;

/// <summary>
/// The result of a command that answers nothing.
///
/// <para>
/// A type rather than <c>void</c>, so <c>ICommand&lt;T&gt;</c> stays one
/// interface. <c>NoResult.Value</c> is the only instance.
///
/// <para>
/// Not <c>Unit</c>, the usual name for this: a unit in this codebase is a unit
/// of measure, and <c>Quantity</c> carries one. Not <c>Nothing</c> either —
/// that is a Visual Basic keyword, and CA1716 rejects it.
/// </para>
/// </para>
/// </summary>
public readonly record struct NoResult
{
    /// <summary>The only value there is.</summary>
    public static NoResult Value => default;
}
