using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace NoitaCraft
{
    // Draws the player as Mina, frame by frame from Noita's own player.png/player.xml, while Mina's set is picked.
    internal sealed class MinaLook : MonoBehaviour
    {
        static Sprite[] idle, move;
        static float idleWait, moveWait;
        static bool loadFailed;

        SkeletonAnimation spine;
        SpriteRenderer body;
        MeshRenderer spineRenderer;
        Vector3 lastPos;
        float clock, movingFor;
        bool reported;

        public static bool Shown;

        static bool Frames()
        {
            if (idle != null) return true;
            if (loadFailed || !NoitaData.Ready) return false;
            try
            {
                string png;
                float ox, oy;
                Dictionary<string, NoitaData.Anim> anims = NoitaData.SpriteAnims(Sheets.Mina.SpriteXml, out png, out ox, out oy);
                Texture2D sheet = NoitaResources.Png(png);
                idle = Cut(sheet, anims[Sheets.Mina.IdleAnim], ox, oy, out idleWait);
                move = Cut(sheet, anims[Sheets.Mina.MoveAnim], ox, oy, out moveWait);
                return true;
            }
            catch (Exception e)
            {
                loadFailed = true;
                Plugin.Log.LogError("Mina's frames: " + e);
                TestRun.Report("mina-look", false, "Mina's frames couldn't be read: " + e.Message);
                return false;
            }
        }

        static Sprite[] Cut(Texture2D sheet, NoitaData.Anim a, float ox, float oy, out float wait)
        {
            wait = Mathf.Max(0.05f, a.FrameWait);
            float ppu = a.FrameHeight / Sheets.Mina.WorldHeight;
            var frames = new Sprite[a.FrameCount];
            for (int f = 0; f < a.FrameCount; f++)
            {
                int x = a.PosX + f % a.FramesPerRow * a.FrameWidth;
                int yTop = a.PosY + f / a.FramesPerRow * a.FrameHeight;
                var r = new Rect(x, sheet.height - yTop - a.FrameHeight, a.FrameWidth, a.FrameHeight);
                // Noita's offset is the sprite's origin, in pixels from the frame's top left
                var pivot = new Vector2(ox / a.FrameWidth, 1f - oy / a.FrameHeight);
                frames[f] = Sprite.Create(sheet, r, pivot, ppu, 0, SpriteMeshType.FullRect);
                frames[f].name = "Mina_" + f;
            }
            return frames;
        }

        static bool MinaPicked => Hooks.SetAdded && DataMgr.selectedWorldData != null && DataMgr.selectedWorldData.selectedSetID == Sheets.Mina.SetId;

        // the player's own Spine (rendered by Magicraft's player camera into a render texture), else the skin manager's
        static SkeletonAnimation Target()
        {
            PlayerMgr pm = PlayerMgr.Inst;
            SkeletonAnimation sa = null;
            try { if (pm != null && pm.PlayerCtrller != null) sa = pm.PlayerCtrller.SAnima; } catch (Exception) { }
            if (sa == null && PlayerSkinMgr.Inst != null) sa = PlayerSkinMgr.Inst.sAnima;
            return sa;
        }

        public static void Attach()
        {
            SkeletonAnimation sa = Target();
            if (sa == null || sa.GetComponent<MinaLook>() != null) return;
            sa.gameObject.AddComponent<MinaLook>();
            Plugin.Log.LogInfo("Mina's look on " + Path(sa.transform) + " (layer " + LayerMask.LayerToName(sa.gameObject.layer) + ")");
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        void Awake()
        {
            spine = GetComponent<SkeletonAnimation>();
            spineRenderer = GetComponent<MeshRenderer>();
            lastPos = transform.position;
        }

        void LateUpdate()
        {
            bool on = MinaPicked && Frames();
            if (!on)
            {
                if (body != null) { body.enabled = false; if (spineRenderer != null) spineRenderer.forceRenderingOff = false; }
                return;
            }
            if (body == null)
            {
                var go = new GameObject("NoitaCraft_Mina");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer; // Magicraft's player camera only draws the player's layer
                body = go.AddComponent<SpriteRenderer>();
            }
            body.enabled = true;
            if (spineRenderer != null) { body.sortingLayerID = spineRenderer.sortingLayerID; body.sortingOrder = spineRenderer.sortingOrder + 1; }
            // the Spine wizard stays (its bones carry the wand), just unseen
            if (spineRenderer != null) spineRenderer.forceRenderingOff = true;

            Vector3 p = transform.position;
            float speed = Time.deltaTime > 0f ? (p - lastPos).magnitude / Time.deltaTime : 0f;
            lastPos = p;
            movingFor = speed > 0.3f ? 0.15f : movingFor - Time.deltaTime;
            bool moving = movingFor > 0f;
            Sprite[] frames = moving ? move : idle;
            clock += Time.deltaTime;
            body.sprite = frames[(int)(clock / (moving ? moveWait : idleWait)) % frames.Length];
            // face where the wizard faces; undo the parent's flip and scale so Mina keeps her size
            float sx = spine.skeleton != null ? Mathf.Sign(spine.skeleton.ScaleX) : 1f;
            Vector3 ls = transform.lossyScale;
            float facing = sx * Mathf.Sign(ls.x == 0f ? 1f : ls.x);
            body.transform.localScale = new Vector3(facing / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)), 1f);
            if (body.isVisible)
            {
                Shown = true;
                if (!reported) { reported = true; Plugin.Log.LogInfo("Mina drawn at " + body.bounds.center + ", size " + body.bounds.size); }
            }
        }

        // Her card on Magicraft's set picker.
        public static void DressCard(UISetSlot slot)
        {
            if (!Frames()) return;
            slot.sGraphic.enabled = false;
            Transform old = slot.transform.Find("NoitaCraft_MinaCard");
            Image img = old != null ? old.GetComponent<Image>() : null;
            if (img == null)
            {
                var go = new GameObject("NoitaCraft_MinaCard", typeof(RectTransform));
                go.transform.SetParent(slot.sGraphic.transform.parent, false);
                go.transform.SetSiblingIndex(slot.sGraphic.transform.GetSiblingIndex());
                img = go.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                go.AddComponent<MinaCard>();
            }
            var rt = (RectTransform)img.transform;
            var src = (RectTransform)slot.sGraphic.transform;
            rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.anchoredPosition = src.anchoredPosition;
            Sprite f = idle[0];
            rt.pivot = new Vector2(f.pivot.x / f.rect.width, f.pivot.y / f.rect.height);
            rt.sizeDelta = new Vector2(Sheets.Mina.UiHeight * f.rect.width / f.rect.height, Sheets.Mina.UiHeight);
            img.sprite = f;
            img.color = slot.sGraphic.color;
        }

        // keeps her card animated and tinted like the others (black while not chosen)
        sealed class MinaCard : MonoBehaviour
        {
            Image img;
            float t;
            void Awake() { img = GetComponent<Image>(); }
            void Update()
            {
                if (idle == null) return;
                t += Time.unscaledDeltaTime;
                img.sprite = idle[(int)(t / idleWait) % idle.Length];
                UISetSlot slot = GetComponentInParent<UISetSlot>();
                if (slot != null) img.color = slot.sGraphic.color;
            }
        }
    }
}
