﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Money.Web2.Common;
using Money.Web2.Components.Operations;
using Money.Web2.Models;
using System.Globalization;

namespace Money.Web2.Layout;

public partial class OperationsLayout : IDisposable
{
    private OperationsFilter? _operationsFilter;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private string PeriodString { get; set; } = GetPeriodString(null, null);
    private List<(OperationTypes.Value type, decimal amount)> Operations { get; } = [];

    public void Dispose()
    {
        if (_operationsFilter != null)
        {
            _operationsFilter.OnSearch -= OnSearchChanged;
        }

        NavigationManager.LocationChanged -= OnLocationChanged;

        GC.SuppressFinalize(this);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender == false)
        {
            return;
        }

        if (_operationsFilter != null)
        {
            _operationsFilter.OnSearch += OnSearchChanged;
        }

        NavigationManager.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = _operationsFilter?.SearchAsync();
    }

    private void OnSearchChanged(object? sender, OperationSearchEventArgs args)
    {
        Operations.Clear();

        foreach (OperationTypes.Value operationType in OperationTypes.Values)
        {
            decimal? amount = args.Operations?
                .Where(x => x.Category.OperationType == operationType)
                .Sum(operation => operation.Sum);

            Operations.Add((operationType, amount ?? 0));
        }

        PeriodString = GetPeriodString(_operationsFilter?.DateRange.Start, _operationsFilter?.DateRange.End);
        StateHasChanged();
    }

    private static string GetPeriodString(DateTime? dateFrom, DateTime? dateTo)
    {
        return $"Период с {FormatDate(dateFrom)} "
               + $"по {FormatDate(dateTo)}";

        string FormatDate(DateTime? date) => date?.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) ?? "-";
    }
}
