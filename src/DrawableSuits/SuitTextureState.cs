using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitTextureState
{
    public int SuitId;
    public ulong OwnerClientId;
    public bool IsPlayerSpecific;
    public string SuitName;
    public Material OriginalMaterial;
    public Texture OriginalTexture;
    public Material RuntimeMaterial;
    public Texture2D BaseTexture;
    public Texture2D EditableTexture;
    public string ActiveDesignName;
}
