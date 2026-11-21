namespace Database.Models;

/// <summary>
/// Within a Two-way Binding, which way one Field Mapping carries values.
/// </summary>
/// <remarks>
/// What is stored is the user's choice. A direction forced by what either side can accept outranks it and is
/// computed, never stored.
/// </remarks>
public enum FieldDirection {
    TwoWay = 0,
    InboundOnly = 1,
    WritebackOnly = 2
}
