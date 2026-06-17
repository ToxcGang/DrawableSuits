using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitTextureState
{
    public int SuitId;
    public ulong OwnerClientId;
    public int OwnerPlayerSlot = -1;
    public bool IsPlayerSpecific;
    public bool IsLocalPlayerState;
    public string SuitName;
    public Material OriginalMaterial;
    public Texture OriginalTexture;
    public Material RuntimeMaterial;
    public Texture2D BaseTexture;
    public Texture2D EditableTexture;
    public string ActiveDesignName;
}
