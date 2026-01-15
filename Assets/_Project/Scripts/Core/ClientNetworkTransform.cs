using Unity.Netcode.Components;
using UnityEngine;

// Ez teszi lehetõvé, hogy a Kliens mozgassa a saját karakterét, 
// és a Szerver ezt elfogadja (ne rángassa vissza).
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // Azt mondjuk, hogy NEM a szerver a fõnök a mozgásban, hanem a tulajdonos (Kliens).
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}