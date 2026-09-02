namespace Matraca.Core;

public sealed record TextDeliveryRequest(
    string Text,
    bool PressEnter,
    TextDeliveryMethod Method,
    TargetToken? Target = null);
