using System;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Голограмма Капри. Создаётся программно (без внешних ассетов — мод ставится одним DLL):
    ///  • «тело» — полупрозрачный голубой цилиндр с глитч-материалом (emissive, alpha-блики);
    ///  • «лицо» — спрайт с глазами (процедурная текстура 128x32), моргает и следит за игроком;
    ///  • имя «CAPRI» над головой (GUI-текстура, pink-cyan как в аниме).
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

            // --- имя CAPRI сверху (процедурная bitmap-текстура, без TMP) ---
            CreateNameLabel("CAPRI");

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

        // ===================== процедурная подпись (текстура, billboard-объект) =====================
        private GameObject _labelBillboard;

        /// <summary>
        /// Генерирует bitmap-текстуру с надписью из 5x7-пиксельного шрифта — без TMP/шрифтовых
        /// ассетов. Показывается billboard-квадом с Unlit-шейдером (стабильно в IL2CPP).
        /// </summary>
        private void CreateNameLabel(string text)
        {
            var tex = MakeLabelTexture(text);
            _labelBillboard = new GameObject("NameLabel");
            _labelBillboard.transform.SetParent(transform, false);
            _labelBillboard.transform.localPosition = new Vector3(0, 2.05f, 0);
            _labelBillboard.transform.localScale = new Vector3(0.9f, 0.22f, 1f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Quad";
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            quad.transform.SetParent(_labelBillboard.transform, false);

            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.mainTexture = tex;
            quad.GetComponent<Renderer>().material = mat;
        }

        private static Texture2D MakeLabelTexture(string text)
        {
            // Рисуем буквы побитовой маской 5x7.
            var rows = new System.Collections.Generic.Dictionary<char, string[]>
            {
                ['A'] = new[]{ "01110","10001","10001","11111","10001","10001","10001" },
                ['B'] = new[]{ "11110","10001","10001","11110","10001","10001","11110" },
                ['C'] = new[]{ "01111","10000","10000","10000","10000","10000","01111" },
                ['P'] = new[]{ "11111","10001","10001","11111","10000","10000","10000" },
                ['R'] = new[]{ "11110","10001","10001","11110","10100","10010","10001" },
                ['I'] = new[]{ "11111","00100","00100","00100","00100","00100","11111" },
                [' '] = new[]{ "00000","00000","00000","00000","00000","00000","00000" },
            };
            int w = 256, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var clear = new Color(0, 0, 0, 0);
            var ink = new Color(1f, 0.31f, 0.85f, 1f); // #FF4FD8 — розовый Капри
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);

            string s = text.ToUpperInvariant();
            int glyphW = 5, gap = 1, scale = 6;
            int totalW = s.Length * (glyphW + gap) - gap;
            int startX = (w - totalW * scale) / 2;
            int startY = (h - 7 * scale) / 2;
            for (int ci = 0; ci < s.Length; ci++)
            {
                if (!rows.TryGetValue(s[ci], out var g)) continue;
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                        if (g[r][c] == '1')
                            for (int dy = 0; dy < scale; dy++)
                                for (int dx = 0; dx < scale; dx++)
                                {
                                    int px = startX + (ci * (glyphW + gap) + c) * scale + dx;
                                    int py = startY + (6 - r) * scale + dy;
                                    if (px >= 0 && px < w && py >= 0 && py < h) tex.SetPixel(px, py, ink);
                                }
            }
            tex.Apply();
            return tex;
        }

        private void LateUpdate()
        {
            // billboard имени — всегда лицом к игроку
            if (_labelBillboard != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 dir = _labelBillboard.transform.position - cam.transform.position;
                    if (dir.sqrMagnitude > 0.001f)
                        _labelBillboard.transform.rotation = Quaternion.LookRotation(dir);
                }
            }
        }
    }
}
