using System;

namespace DrawableSuits;

[Serializable]
internal sealed class SuitDesignMetadata
{
    public string version;
    public string name;
    public string baseSuitName;
    public int sourceSuitId;
    public string textureFile;
    public int width;
    public int height;
    public string createdUtc;
    public string updatedUtc;
}
