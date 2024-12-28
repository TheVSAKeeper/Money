using Microsoft.FluentUI.AspNetCore.Components;
using Money.Web2.Models;

namespace Money.Web2.Common;

public static class TreeViewExtensions
{
    public static IEnumerable<ITreeViewItem> BuildChildren(this List<Category> categories, int? parentId)
    {
        return categories.Where(category => category.ParentId == parentId)
            .Select(child => new TreeViewItem
            {
                Text = child.Name,
                //Value = child,
                Items = BuildChildren(categories, child.Id),
            })
            //.OrderBy(item => item.Value?.Order == null)
            //.ThenBy(item => item.Value?.Order)
            //.ThenBy(item => item.Value?.Name)
            .ToList();
    }

    public static void Filter(this IEnumerable<ITreeViewItem> treeItemData, string text)
    {
        foreach (ITreeViewItem itemData in treeItemData)
        {
            if (itemData.Items != null && itemData.Items.Any())
            {
                Filter(itemData.Items, text);
            }

            itemData.Disabled = itemData.IsVisible(text);
        }
    }

    public static bool IsVisible(this ITreeViewItem itemData, string searchTerm)
    {
        if (itemData.Items != null && itemData.Items.Any())
        {
            return itemData.Text.IsMatch(searchTerm)
                   || itemData.Items.Any(child => child.Text.IsMatch(searchTerm));
        }

        return itemData.Text.IsMatch(searchTerm);
    }

    public static bool IsMatch(this string? text, string searchTerm)
    {
        return string.IsNullOrEmpty(text) == false && text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }
}
