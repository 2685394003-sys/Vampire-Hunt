using UnityEngine;

public static class TopDownPrototype
{
    public static Sprite GetDefaultSprite()
    {
        return Resources.Load<Sprite>("DefaultSquare");
    }
}
