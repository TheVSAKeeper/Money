﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Money.Web2.Common;
using Money.Web2.Models;
using System.Drawing;

namespace Money.Web2.Components.Operations;

public partial class OperationsDayCard
{
    private OperationDialog _dialog = null!;

    [CascadingParameter]
    public required AppSettings Settings { get; set; }

    [Parameter]
    public required OperationsDay OperationsDay { get; set; }

    [Parameter]
    public required OperationTypes.Value[] OperationTypes { get; set; }

    [Parameter]
    public required List<FastOperation> FastOperations { get; set; }

    [Parameter]
    public EventCallback<Operation> OnAddOperation { get; set; }

    [Parameter]
    public EventCallback<Operation> OnRestore { get; set; }

    [Parameter]
    public EventCallback<Operation> OnDelete { get; set; }

    [Parameter]
    public EventCallback<OperationsDay> OnCanDelete { get; set; }





    private async Task OnSubmit(Operation operation)
    {
        if (operation.Date == OperationsDay.Date)
        {
            OperationsDay.Operations.Add(operation);
        }
        else
        {
            await OnAddOperation.InvokeAsync(operation);
        }
    }

    private async Task OnEdit(Operation operation)
    {
        if (operation.Date == OperationsDay.Date)
        {
            StateHasChanged();
            return;
        }

        OperationsDay.Operations.Remove(operation);

        if (OperationsDay.Operations.Count == 0)
        {
            await OnCanDelete.InvokeAsync(OperationsDay);
        }

        await OnAddOperation.InvokeAsync(operation);
        StateHasChanged();
    }
}
