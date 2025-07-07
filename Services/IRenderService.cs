using System;
using SharpDX;

namespace PickIt.Services;

public interface IRenderService : IDisposable
{
    void RenderInventoryOverlay();
    void RenderDebugHighlights();
    void RenderItemHighlight(PickItItemData item, Color color);
    void RenderAll();
} 