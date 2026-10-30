using System;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using TMPro;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Голограмма Капри. Создаётся программно (без внешних ассетов — мод ставится одним DLL):
    ///  • «тело» — полупрозрачный голубой цилиндр с глитч-материалом (emissive, alpha-блики);
    ///  • «лицо» — спрайт с глазами (процедурная текстура 128x32), моргает и следит за игроком;
    ///  • имя «CAPRI» над головой (TextMeshPro, pink-cyan как в аниме).
    ///
    /// Видимость для других игроков выключается через слой (см. CapriService.SetVisible):
    /// Капри видна только носителю чипа.
    /// </summary>
    public class CapriEntity : MonoBehaviour
    {
        public static CapriEntity Instance { get; private set; }

        public bool Visible { get; private set; } = true;

        private Transform _visualRoot;
        private SpriteRenderer _face;
        private TMP_Text _nameLabel;
        private Material _glitchMat;
        private float _bornTime;
        private float _nextBlink;
        private bool _blinkOn;
        private Texture2D _eyesTexOpen, _eyesTexClosed;

        // ===================== создание =====================
        public static CapriEntity Spawn(Vector3 position, Quaternion lookDir)
        {
            Despawn();
            var go = new GameObject("CapriEntity");
            go.transform.position = position;
            go.transform.rotation = lookDir;
            var e = go.AddComponent<CapriEntity>();
            e.BuildVisuals();
            Instance = e;
            return e;
        }

        public static void Despawn()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            Instance = null;
        }

        private void BuildVisuals()
        {
            _bornTime = Time.time;

            // --- тело: тонкий голубой цилиндр ("проекция") ---
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.55f, 0.95f, 0.18f);
            body.transform.localPosition = new Vector3(0, 0.95f, 0);
            Object.Destroy(body.GetComponent<Collider>());

            var shader = Shader.Find("Standard");
            _glitchMat = new Material(shader);
            _glitchMat.color = new Color(0.25f, 0.75f, 1f, 0.45f);
            _glitchMat.SetFloat("_Mode", 3); // transparent
            SetBlendMode(_glitchMat);
            body.GetComponent<Renderer>().material = _glitchMat;

            // --- лицо/глаза: billboard-спрайт на уровне головы ---
            _eyesTexOpen = MakeEyesTexture(true);
            _eyesTexClosed = MakeEyesTexture(false);

            var faceGO = new GameObject("Face");
            faceGO.transform.SetParent(transform, false);
            faceGO.transform.localPosition = new Vector3(0, 1.62f, 0.12f);
            faceGO.transform.localScale = new Vector3(0.6f, 0.16f, 1f);
            _face = faceGO.AddComponent<SpriteRenderer>();
            _face.sprite = Sprite.Create(_eyesTexOpen, new Rect(0, 0, 128, 32), new Vector2(0.5f, 0.5f));
            _face.sortingOrder = 10;

            // --- имя CAPRI сверху ---
            var labelGO = new GameObject("NameLabel");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0, 2.05f, 0);
            _nameLabel = labelGO.AddComponent<TextMeshPro>();
            _nameLabel.text = "<color=#FF4FD8>CAPRI</color>";
            _nameLabel.fontSize = 0.4f;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.enableWordWrapping = false;
            _nameLabel.color = Color.white;

            // стартовая ориентация — лицом к игроку
            FacePlayer();

            // скрыть от всех, кроме носителя чипа (слой)
            CapriService.SetVisible(gameObject, true);
            Visible = true;
        }

        private static void SetBlendMode(Material m)
        {
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.renderQueue = 3000;
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
        }

        // ===================== жизненный цикл =====================
        private void Update()
        {
            if (!Visible) return;

            // лёгкое парение + глитч-мерцание
            float t = Time.time - _bornTime;
            float bob = Mathf.Sin(t * 1.7f) * 0.03f;
            float flicker = 0.35f + 0.1f * Mathf.Sin(t * 9f) + (Mathf.Sin(t * 31f) > 0.985f ? -0.25f : 0f);
            if (_glitchMat != null)
            {
                var c = _glitchMat.color;
                c.a = Mathf.Clamp(flicker, 0.08f, 0.6f);
                _glitchMat.color = c;
            }
            if (_visualRoot == null) _visualRoot = transform;
            var body = _visualRoot.Find("Body");
            if (body != null) body.localPosition = new Vector3(0, 0.95f + bob, 0);

            // поворот к игроку
            FacePlayer();

            // моргание
            if (Time.time > _nextBlink)
            {
                _blinkOn = !_blinkOn;
                _nextBlink = Time.time + (_blinkOn ? 0.12f : UnityEngine.Random.Range(2.2f, 4.5f));
                if (_face != null)
                    _face.sprite = Sprite.Create(_blinkOn ? _eyesTexClosed : _eyesTexOpen,
                        new Rect(0, 0, 128, 32), new Vector2(0.5f, 0.5f));
            }
        }

        private void FacePlayer()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = cam.transform.position - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>Точка, от которой звучит голос (условно — голова голограммы).</summary>
        public Transform VoiceAnchor => transform;

        public void Hide()
        {
            Visible = false;
            CapriService.SetVisible(gameObject, false);
        }

        public void Show()
        {
            Visible = true;
            CapriService.SetVisible(gameObject, true);
        }

        // ===================== процедурные глаза =====================
        private static Texture2D MakeEyesTexture(bool open)
        {
            int w = 128, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var clear = new Color(0, 0, 0, 0);
            Color iris = new Color(0.35f, 0.9f, 1f, 1f);     // голубые глаза-голограмма
            Color pupil = new Color(0.05f, 0.1f, 0.2f, 1f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);

            void Disc(int cx, int cy, int r, Color c)
            {
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r && x >= 0 && x < w && y >= 0 && y < h)
                            tex.SetPixel(x, y, c);
            }

            if (open)
            {
                Disc(40, 16, 9, iris); Disc(40, 16, 4, pupil);
                Disc(88, 16, 9, iris); Disc(88, 16, 4, pupil);
                // блики
                Disc(43, 19, 2, Color.white); Disc(91, 19, 2, Color.white);
            }
            else
            {
                for (int x = 30; x < 50; x++) tex.SetPixel(x, 16, iris);
                for (int x = 78; x < 98; x++) tex.SetPixel(x, 16, iris);
            }
            tex.Apply();
            return tex;
        }
    }
}
