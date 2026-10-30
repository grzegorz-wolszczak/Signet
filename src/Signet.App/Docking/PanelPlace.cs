using System;
using System.Collections.Generic;
using Signet.App.Actions;

namespace Signet.App.Docking;

/// <summary>
/// Where a panel sits in the main window (IntelliJ's tool window anchor with its split half): one of six places, each
/// an area of tabs of its own. The left and the right column are split into a top and a bottom half, the bottom row
/// (along the whole window) into a left and a right half. A half with no shown panel takes no room.
/// </summary>
public enum PanelPlace
{
    /// <summary>The top half of the left column.</summary>
    LeftTop,

    /// <summary>The bottom half of the left column.</summary>
    LeftBottom,

    /// <summary>The left half of the bottom row.</summary>
    BottomLeft,

    /// <summary>The right half of the bottom row.</summary>
    BottomRight,

    /// <summary>The bottom half of the right column.</summary>
    RightBottom,

    /// <summary>The top half of the right column.</summary>
    RightTop,
}

/// <summary>The places of the panels: their docks, their stripes, their names and icons.</summary>
public static class PanelPlaces
{
    /// <summary>Every place, in the order of the "Move To" menu (IntelliJ's: around the window, from the top left).</summary>
    public static IReadOnlyList<PanelPlace> All { get; } = Enum.GetValues<PanelPlace>();

    /// <summary>The id of the dock (area of tabs) of a place in the dock layout.</summary>
    public static string DockId(PanelPlace place) => place + "Dock";

    /// <summary>The place of a dock id (<see cref="DockId"/>), also the ids of the three regions of the layouts saved
    /// before the places were split (LeftDock, RightDock, BottomDock): the first half of each.</summary>
    public static bool TryParseDockId(string dockId, out PanelPlace place)
    {
        ArgumentNullException.ThrowIfNull(dockId);
        switch (dockId)
        {
            case LegacyLeftDock:
                place = PanelPlace.LeftTop;
                return true;
            case LegacyRightDock:
                place = PanelPlace.RightTop;
                return true;
            case LegacyBottomDock:
                place = PanelPlace.BottomLeft;
                return true;
        }

        foreach (PanelPlace candidate in All)
        {
            if (dockId == DockId(candidate))
            {
                place = candidate;
                return true;
            }
        }

        place = default;
        return false;
    }

    /// <summary>The id of the left region of a layout saved before the places were split.</summary>
    public const string LegacyLeftDock = "LeftDock";

    /// <summary>The id of the right region of a layout saved before the places were split.</summary>
    public const string LegacyRightDock = "RightDock";

    /// <summary>The id of the bottom region of a layout saved before the places were split.</summary>
    public const string LegacyBottomDock = "BottomDock";

    /// <summary>The stripe that holds the buttons of the panels of a place: the left one for the left column and the
    /// left half of the bottom row, the right one for the others (as in IntelliJ).</summary>
    public static PanelStripeSide SideOf(PanelPlace place) =>
        place is PanelPlace.LeftTop or PanelPlace.LeftBottom or PanelPlace.BottomLeft ? PanelStripeSide.Left : PanelStripeSide.Right;

    /// <summary>The resource key of the name of a place (the "Move To" menu).</summary>
    public static string NameKey(PanelPlace place) => "PanelPlace_" + place;

    /// <summary>The icon of a place (a window with that part filled).</summary>
    public static string IconKey(PanelPlace place) => place switch
    {
        PanelPlace.LeftTop => ActionIconKeys.PanelPlaceLeftTop,
        PanelPlace.LeftBottom => ActionIconKeys.PanelPlaceLeftBottom,
        PanelPlace.BottomLeft => ActionIconKeys.PanelPlaceBottomLeft,
        PanelPlace.BottomRight => ActionIconKeys.PanelPlaceBottomRight,
        PanelPlace.RightBottom => ActionIconKeys.PanelPlaceRightBottom,
        _ => ActionIconKeys.PanelPlaceRightTop,
    };
}

/// <summary>A stripe of panel buttons along a side of the main window.</summary>
public enum PanelStripeSide
{
    /// <summary>The stripe along the left side.</summary>
    Left,

    /// <summary>The stripe along the right side.</summary>
    Right,
}
