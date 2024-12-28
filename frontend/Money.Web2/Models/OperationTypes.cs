using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components.Icons.Regular;

namespace Money.Web2.Models;

public static class OperationTypes
{
    public static readonly Value None = new(0, "Неизвестный тип", new Size20.ErrorCircle(), Color.Error);

    public static Value[] Values { get; } = GetValues();

    private static Value[] GetValues()
    {
        return
        [
            new Value(1, "Расходы", new Size20.ArrowCircleDown(), Color.Warning),
            new Value(2, "Доходы", new Size20.ArrowCircleUp(), Color.Success),
        ];
    }

    public record Value(int Id, string Name, Icon Icon, Color Color)
    {
        public string AddText { get; } = "Добавить " + Name.ToLower();
    }
}
