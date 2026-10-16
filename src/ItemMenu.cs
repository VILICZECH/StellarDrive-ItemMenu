using MelonLoader;
using StellarDriveMods.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(ItemMenu.ItemMenuMod), "Item menu", "1.0.0", "VILIK")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace ItemMenu
{
    public class ItemMenuMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Item menu is ready. Press I to open materials.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            ItemWindow.CloseCurrent();
        }

        public override void OnUpdate()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (ItemWindow.IsOpen)
            {
                if (keyboard.iKey.wasPressedThisFrame && GameKeys.FreeInOwnWindow())
                    ItemWindow.CloseCurrent();
                return;
            }

            // I opens the window only while the player plays, never from the chat, a text field or another menu.
            if (keyboard.iKey.wasPressedThisFrame && GameKeys.Free())
                ItemWindow.Open();
        }
    }
}
