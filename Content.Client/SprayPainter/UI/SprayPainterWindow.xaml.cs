using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Utility;

namespace Content.Client.SprayPainter.UI;

[GenerateTypedNameReferences]
public sealed partial class SprayPainterWindow : DefaultWindow
{
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;
    private readonly SpriteSystem _spriteSystem;

    public Action<ItemList.ItemListSelectedEventArgs>? OnSpritePicked;
    public Action<ItemList.ItemListSelectedEventArgs>? OnColorPicked;
    public Dictionary<string, int> ItemColorIndex = new();

    private Dictionary<string, Color> currentPalette = new();
    private const string colorLocKeyPrefix = "pipe-painter-color-";
    private List<SprayPainterEntry> CurrentEntries = new List<SprayPainterEntry>();

    private readonly SpriteSpecifier _colorEntryIconTexture = new SpriteSpecifier.Rsi(
        new ResPath("Structures/Piping/Atmospherics/pipe.rsi"),
        "pipeStraight");

    public SprayPainterWindow()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);
        _spriteSystem = _sysMan.GetEntitySystem<SpriteSystem>();
    }

    private static string GetColorLocString(string? colorKey)
    {
        if (string.IsNullOrEmpty(colorKey))
            return Loc.GetString("pipe-painter-no-color-selected");
        var locKey = colorLocKeyPrefix + colorKey;

        if (!Loc.TryGetString(locKey, out var locString))
            locString = colorKey;

        return locString;
        }

    public string? IndexToColorKey(int index)
    {
        return (string?) ColorList[index].Metadata;
    }

    public void Populate(List<SprayPainterEntry> entries, int selectedStyle, string? selectedColorKey, Dictionary<string, Color> palette)
    {
        // Only clear if the entries change. Otherwise the list would "jump" after selecting an item
        if (!CurrentEntries.Equals(entries))
        {
            CurrentEntries = entries;
            SpriteList.Clear();
            foreach (var entry in entries)
            {
                SpriteList.AddItem(entry.Name, entry.Icon);
            }
        }

        if (!currentPalette.Equals(palette))
        {
            currentPalette = palette;
            ItemColorIndex.Clear();
            ColorList.Clear();

            foreach (var color in palette)
            {
                var locString = GetColorLocString(color.Key);
                var item = ColorList.AddItem(locString, _spriteSystem.Frame0(_colorEntryIconTexture));
                item.IconModulate = color.Value;
                item.Metadata = color.Key;

                ItemColorIndex.Add(color.Key, ColorList.IndexOf(item));
            }
        }

        // Disable event so we don't send a new event for pre-selectedStyle entry and end up in a loop

        if (selectedColorKey != null)
        {
            var index = ItemColorIndex[selectedColorKey];
            ColorList.OnItemSelected -= OnColorPicked;
            ColorList[index].Selected = true;
            ColorList.OnItemSelected += OnColorPicked;
        }

        SpriteList.OnItemSelected -= OnSpritePicked;
        SpriteList[selectedStyle].Selected = true;
        SpriteList.OnItemSelected += OnSpritePicked;
    }
}