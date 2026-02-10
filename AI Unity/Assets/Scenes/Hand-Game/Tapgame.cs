using System.Collections;
using System.Collections.Generic;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Unity.Sample;
using UnityEngine;
using TMPro;

public class Tapgame : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private GameObject prefab; // assign Target.prefab; auto-load if left empty
    [SerializeField] private Transform parent;
    [SerializeField] private Vector3 areaMin = new Vector3(-10f, -3f, 0f);
    [SerializeField] private Vector3 areaMax = new Vector3(10f, 5f, 0f);
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private int spawnCount = 10;
    [SerializeField] private float minSpacing = 1f; // minimum distance between spawned objects
    [SerializeField] private int maxSpawnAttempts = 50;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private float tapRadius = 0.3f; // radius to detect fingertip hit
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip destroyClip;
    [Header("Score")]
    [SerializeField] private int scorePerHit = 1;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text startText;
    [SerializeField] private TMP_Text accuracyText;
    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private float totalTimeSeconds = 60f;
    [Header("Lifetime")]
    [SerializeField] private float lifetimeSeconds = 5f;
    [SerializeField] private float lifetimeJitter = 2f;
    [SerializeField] private float shrinkDuration = 1.5f;
    [SerializeField] private float minShrinkScale = 0.02f;
    [Header("Chain Disappear")]
    [SerializeField] private int chainDisappearCount = 3;
    [SerializeField] private float chainMinDelay = 0.5f;
    [SerializeField] private float chainMaxDelay = 2.5f;
    [Header("Hand Tracking")]
    [SerializeField] private HandLandmarkerRunner runner;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private int landmarkIndex = 8; // fingertip
    [SerializeField] private bool useWorldLandmarks = true;
    [SerializeField] private float normalizedDepth = 1.0f; // meters used when useWorldLandmarks is false
    [SerializeField] private bool projectFingerToPlane = true;
    [SerializeField] private float targetPlaneZ = 0f; // world Z plane where targets live (areaMin/Max.z)
    [SerializeField] private float fingerDepthFromCamera = 1.0f; // depth along camera forward to place fingertip when using normalized UV

    private readonly List<Vector3> _spawnedPositions = new List<Vector3>();
    private readonly List<SpawnedData> _activeSpawns = new List<SpawnedData>();
    private readonly object _resultLock = new object();
    private HandLandmarkerResult _latestResult;
    private bool _hasResult;
    private int _score;
    private bool _gameStarted;
    private bool _chainTriggered;
    private bool _gameEnded;
    private float _timeRemaining;
    private int _totalSpawned;
    private int _hitCount;
    private int _missCount;
    private bool _startedOnce;

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        // Auto-load Target.prefab from Resources if not set in Inspector.
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>("Target");
            if (prefab == null)
            {
                // Debug.LogWarning("Tapgame: prefab not assigned and Target.prefab not found in Resources.");
            }
        }

        ResetGameState(respawnTargets: true);

        // Ensure webcam/image source is running when this game object becomes active.
        // StartCoroutine(EnsureImageSourceRunning());

        _startedOnce = true;
    }

    /*
    private void SpawnBatch(int targetCount)
    {
     // ... (existing code, wait, I shouldn't replace SpawnBatch here, I should target the Start method logic)
     // I will use multiple chunks.
    }
    */

    // ...

    // private void OnEnable()
    // {
    //     if (runner != null)
    //     {
    //         runner.OnHandResult += HandleHandResult;
    //     }

    //     // Start the image source if it was stopped when switching scenes/objects.
    //     // StartCoroutine(EnsureImageSourceRunning());

    //     // If this is not the first enable (Start already ran), reset and respawn.
    //     if (_startedOnce)
    //     {
    //         ResetGameState(respawnTargets: true);
    //     }
    // }

    // ...

    // Added: ensure webcam/image source restarts when this object is active.
    /*
    private IEnumerator EnsureImageSourceRunning()
    {
      // Ensure we are using the webcam source and it is running.
      if (ImageSourceProvider.CurrentSourceType != ImageSourceType.WebCamera)
      {
        ImageSourceProvider.Switch(ImageSourceType.WebCamera);
      }

      var source = ImageSourceProvider.ImageSource;
      if (source == null)
      {
        yield break;
      }

      if (!source.isPrepared || !source.isPlaying)
      {
        Debug.Log("Tapgame: starting image source for webcam.");
        yield return source.Play();
      }
    }
    */

    private void SpawnBatch(int targetCount)
    {
        int spawned = 0;
        int attempts = 0;
        int attemptLimit = maxSpawnAttempts * Mathf.Max(1, targetCount);

        while (spawned < targetCount && attempts < attemptLimit)
        {
            if (SpawnRandom() != null)
            {
                spawned++;
            }
            attempts++;
        }

        if (spawned < targetCount)
        {
            // Debug.LogWarning($"Tapgame: Requested {targetCount} spawns, placed {spawned} (min spacing may be too large for the area).");
        }
    }

    public GameObject SpawnRandom()
    {
        if (prefab == null) return null;

        if (minSpacing < 0f)
        {
            minSpacing = 0f;
        }

        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            var randomPos = new Vector3(
              Random.Range(areaMin.x, areaMax.x),
              Random.Range(areaMin.y, areaMax.y),
              Random.Range(areaMin.z, areaMax.z)
            );

            if (IsFarEnough(randomPos))
            {
                return SpawnInternal(randomPos);
            }
        }

        // Debug.LogWarning("Tapgame: Failed to find a spawn position respecting min spacing.");
        return null;
    }

    public GameObject SpawnClamped(Vector3 desiredPosition)
    {
        if (prefab == null) return null;

        var clamped = new Vector3(
          Mathf.Clamp(desiredPosition.x, areaMin.x, areaMax.x),
          Mathf.Clamp(desiredPosition.y, areaMin.y, areaMax.y),
          Mathf.Clamp(desiredPosition.z, areaMin.z, areaMax.z)
        );

        if (!IsFarEnough(clamped))
        {
            // Debug.LogWarning("Tapgame: Clamped position violates min spacing, not spawning.");
            return null;
        }

        return SpawnInternal(clamped);
    }

    private void OnEnable()
    {
        if (runner != null)
        {
            runner.OnHandResult += HandleHandResult;
        }

        // Start the image source if it was stopped when switching scenes/objects.
        // StartCoroutine(EnsureImageSourceRunning());

        // If this is not the first enable (Start already ran), reset and respawn.
        if (_startedOnce)
        {
            ResetGameState(respawnTargets: true);
        }
    }

    private void OnDisable()
    {
        if (runner != null)
        {
            runner.OnHandResult -= HandleHandResult;
        }

        // Stop all coroutines running on this MonoBehaviour (e.g., ensure image source).
        StopAllCoroutines();

        // Clear state and despawn targets so the game is stopped while this object is inactive.
        ResetGameState(respawnTargets: false);
    }

    // Added: ensure webcam/image source restarts when this object is active.
    private IEnumerator EnsureImageSourceRunning()
    {
        // Ensure we are using the webcam source and it is running.
        if (ImageSourceProvider.CurrentSourceType != ImageSourceType.WebCamera)
        {
            ImageSourceProvider.Switch(ImageSourceType.WebCamera);
        }

        var source = ImageSourceProvider.ImageSource;
        if (source == null)
        {
            yield break;
        }

        if (!source.isPrepared || !source.isPlaying)
        {
            Debug.Log("Tapgame: starting image source for webcam.");
            yield return source.Play();
        }
    }

    private void HandleHandResult(HandLandmarkerResult result)
    {
        lock (_resultLock)
        {
            result.CloneTo(ref _latestResult);
            _hasResult = true;
        }
    }

    private void Update()
    {
        TryTapFromHand();
        if (_gameStarted && !_gameEnded)
        {
            if (_timeRemaining > 0f)
            {
                _timeRemaining = Mathf.Max(0f, _timeRemaining - Time.deltaTime);
                UpdateTimerUI();
                if (_timeRemaining <= 0f)
                {
                    EndGame();
                    return;
                }
            }
            UpdateLifetime(Time.deltaTime);
        }
    }

    private void TryTapFromHand()
    {
        if (targetCamera == null || runner == null)
        {
            // Debug.LogWarning("Tapgame: Missing targetCamera or runner.");
            return;
        }

        HandLandmarkerResult result;
        bool hasResult;
        lock (_resultLock)
        {
            result = _latestResult;
            hasResult = _hasResult;
            _hasResult = false;
        }

        if (!hasResult)
        {
            // Debug.Log("Tapgame: No hand result yet.");
            return;
        }

        var fingerPositions = GetFingerWorldPositions(result);
        if (fingerPositions.Count == 0)
        {
            // Debug.Log("Tapgame: No fingertip position available.");
            return;
        }

        foreach (var fingerPos in fingerPositions)
        {
            var tapPos = projectFingerToPlane ? ProjectToPlaneZ(fingerPos) : fingerPos;
            // Debug.Log($"Tapgame: fingerWorld={fingerPos} tapPos={tapPos} tapRadius={tapRadius} targetLayer={targetLayer.value}");

            if (TryFingerTap(tapPos))
            {
                // Stop after first successful hit.
                return;
            }
        }

        // Debug.Log("Tapgame: No target hit.");
    }

    private List<Vector3> GetFingerWorldPositions(HandLandmarkerResult result)
    {
        var positions = new List<Vector3>();

        // Prefer normalized landmarks to place fingertip along a fixed depth from camera.
        var normList = result.handLandmarks;
        if (normList != null && normList.Count > 0 && normList[0].landmarks != null && normList[0].landmarks.Count > landmarkIndex)
        {
            var depth = projectFingerToPlane
              ? Mathf.Max(0.01f, Mathf.Abs(targetPlaneZ - targetCamera.transform.position.z))
              : Mathf.Max(0.01f, fingerDepthFromCamera);

            for (int i = 0; i < normList.Count; i++)
            {
                var hand = normList[i];
                if (hand.landmarks == null || hand.landmarks.Count <= landmarkIndex) continue;
                var lm = hand.landmarks[landmarkIndex];
                var pos = targetCamera.ViewportToWorldPoint(new Vector3(lm.x, 1f - lm.y, depth));
                positions.Add(pos);
            }

            return positions;
        }

        if (useWorldLandmarks)
        {
            var worldList = result.handWorldLandmarks;
            if (worldList != null && worldList.Count > 0)
            {
                for (int i = 0; i < worldList.Count; i++)
                {
                    var hand = worldList[i];
                    if (hand.landmarks == null || hand.landmarks.Count <= landmarkIndex) continue;
                    var lm = hand.landmarks[landmarkIndex];
                    var cameraSpace = new Vector3(lm.x, lm.y, -lm.z); // camera-centric
                    positions.Add(targetCamera.transform.TransformPoint(cameraSpace));
                }
            }
            return positions;
        }

        return positions;
    }

    private Vector3 ProjectToPlaneZ(Vector3 fingerWorldPos)
    {
        var camPos = targetCamera.transform.position;
        var dir = fingerWorldPos - camPos;
        if (Mathf.Abs(dir.z) < 1e-4f)
        {
            return fingerWorldPos;
        }

        float t = (targetPlaneZ - camPos.z) / dir.z;
        return camPos + dir * t;
    }

    // Call this with the fingertip world position to destroy a nearby target and respawn a new one.
    public bool TryFingerTap(Vector3 fingerWorldPos)
    {
        // Allow tapping even if game ended, to trigger restart
        // if (_gameEnded) { return false; }

        var hits = Physics.OverlapSphere(fingerWorldPos, tapRadius, targetLayer);
        // Debug.Log($"Tapgame: Overlap hits count={hits.Length} at {fingerWorldPos}");
        if (hits.Length == 0)
        {
            return false;
        }

        // Destroy the first hit target.
        var target = hits[0];
        RemoveTrackedSpawn(target.transform);
        RemoveActiveSpawn(target.transform);
        Destroy(target.gameObject);

        // Spawn a replacement.
        SpawnRandom();
        PlayDestroySound();
        AddScore(scorePerHit);
        _hitCount++;
        UpdateAccuracyUI();

        if (!_gameStarted)
        {
            // If game ended previously, this is a restart
            if (_gameEnded)
            {
                _score = 0;
                _hitCount = 0;
                _missCount = 0;
                _totalSpawned = 1; // The one we just spawned in SpawnRandom() above
                UpdateScoreUI();
                UpdateAccuracyUI();

                // Ensure we have the full spawn count for the new game
                int needed = spawnCount - _activeSpawns.Count;
                if (needed > 0)
                {
                    SpawnBatch(needed);
                }
            }

            _gameStarted = true;
            _gameEnded = false;
            UpdateStartPrompt();
            ResetSpawnTimers();
            _timeRemaining = totalTimeSeconds;
            UpdateTimerUI();
        }

        if (!_chainTriggered)
        {
            _chainTriggered = true;
            ScheduleChainDisappear();
        }
        return true;
    }

    private GameObject SpawnInternal(Vector3 position)
    {
        var go = Instantiate(prefab, position, Quaternion.identity, parent);
        _spawnedPositions.Add(go.transform.position);
        _totalSpawned++;
        _activeSpawns.Add(new SpawnedData
        {
            transform = go.transform,
            baseScale = go.transform.localScale,
            age = 0f,
            lifetime = lifetimeSeconds + Random.Range(0f, Mathf.Max(0f, lifetimeJitter)),
            shrinkProgress = 0f,
            shrinking = false,
            forcedExpireAt = -1f
        });
        UpdateAccuracyUI();
        return go;
    }

    private bool IsFarEnough(Vector3 candidate)
    {
        for (int i = 0; i < _spawnedPositions.Count; i++)
        {
            if (Vector3.Distance(candidate, _spawnedPositions[i]) < minSpacing)
            {
                return false;
            }
        }
        return true;
    }

    private void PlayDestroySound()
    {
        if (audioSource != null && destroyClip != null)
        {
            audioSource.PlayOneShot(destroyClip);
        }
    }

    private void AddScore(int delta)
    {
        _score += delta;
        // Debug.Log($"Tapgame: Score = {_score}");
        UpdateScoreUI();
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = $"PTS {_score}";
        }
    }

    private void UpdateAccuracyUI()
    {
        if (accuracyText == null)
        {
            return;
        }

        int attempts = Mathf.Max(1, _totalSpawned);
        float acc = (float)_hitCount / attempts;
        accuracyText.text = $"{acc:P0}";
    }

    private void UpdateStartPrompt()
    {
        if (startText == null)
        {
            return;
        }

        if (_gameEnded)
        {
            startText.gameObject.SetActive(true);
            startText.text = "GAME END / Point Ball to Restart";
        }
        else if (_gameStarted)
        {
            startText.gameObject.SetActive(false);
        }
        else
        {
            startText.gameObject.SetActive(true);
            startText.text = "Point Ball to Start";
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.CeilToInt(_timeRemaining);
        int minutes = Mathf.Max(0, totalSeconds / 60);
        int seconds = Mathf.Max(0, totalSeconds % 60);
        timerText.text = $"{minutes}:{seconds:D2}";
    }

    private void EndGame()
    {
        _gameEnded = true;
        _gameStarted = false;
        _timeRemaining = 0f;
        UpdateTimerUI();
        UpdateStartPrompt();

        // Clear board and spawn a single "Restart Ball"
        for (int i = _activeSpawns.Count - 1; i >= 0; i--)
        {
            if (_activeSpawns[i].transform != null)
            {
                Destroy(_activeSpawns[i].transform.gameObject);
            }
        }
        _activeSpawns.Clear();
        _spawnedPositions.Clear();

        SpawnRandom();
    }

    private void UpdateLifetime(float deltaTime)
    {
        for (int i = _activeSpawns.Count - 1; i >= 0; i--)
        {
            var s = _activeSpawns[i];
            if (s.transform == null)
            {
                _activeSpawns.RemoveAt(i);
                continue;
            }

            s.age += deltaTime;
            if (!s.shrinking && s.forcedExpireAt > 0f && s.age >= s.forcedExpireAt)
            {
                s.shrinking = true;
                s.shrinkProgress = 0f;
            }
            else if (!s.shrinking && s.age >= s.lifetime)
            {
                s.shrinking = true;
                s.shrinkProgress = 0f;
            }

            if (s.shrinking)
            {
                s.shrinkProgress += shrinkDuration > 0f ? deltaTime / shrinkDuration : 1f;
                float factor = Mathf.Max(0f, 1f - s.shrinkProgress);
                s.transform.localScale = s.baseScale * factor;

                if (factor <= minShrinkScale)
                {
                    RemoveTrackedSpawn(s.transform);
                    RemoveActiveSpawn(s.transform);
                    Destroy(s.transform.gameObject);
                    _missCount++;
                    UpdateAccuracyUI();
                    SpawnRandom();
                    continue;
                }
            }

            _activeSpawns[i] = s;
        }
    }

    private void RemoveTrackedSpawn(Transform t)
    {
        if (t == null) return;
        _spawnedPositions.RemoveAll(p => Vector3.Distance(p, t.position) < minSpacing * 0.5f);
    }

    private void RemoveActiveSpawn(Transform t)
    {
        if (t == null) return;
        _activeSpawns.RemoveAll(sp => sp.transform == t);
    }

    // Added: reset state, UI, and optionally respawn targets for a fresh session.
    private void ResetGameState(bool respawnTargets)
    {
        // Destroy any remaining targets.
        for (int i = _activeSpawns.Count - 1; i >= 0; i--)
        {
            var tr = _activeSpawns[i].transform;
            if (tr != null)
            {
                Destroy(tr.gameObject);
            }
        }
        _activeSpawns.Clear();
        _spawnedPositions.Clear();

        _score = 0;
        _hitCount = 0;
        _missCount = 0;
        _totalSpawned = 0;
        _gameStarted = false;
        _gameEnded = false;
        _chainTriggered = false;
        _timeRemaining = totalTimeSeconds;

        UpdateScoreUI();
        UpdateAccuracyUI();
        UpdateStartPrompt();
        UpdateTimerUI();

        if (respawnTargets && spawnOnStart)
        {
            SpawnBatch(spawnCount);
        }
    }

    private void ResetSpawnTimers()
    {
        for (int i = 0; i < _activeSpawns.Count; i++)
        {
            var s = _activeSpawns[i];
            if (s.transform == null) continue;
            s.age = 0f;
            s.shrinkProgress = 0f;
            s.shrinking = false;
            s.forcedExpireAt = -1f;
            s.lifetime = lifetimeSeconds + Random.Range(0f, Mathf.Max(0f, lifetimeJitter));
            s.transform.localScale = s.baseScale;
            _activeSpawns[i] = s;
        }
        _hitCount = 0;
        _missCount = 0;
        _gameEnded = false;
        _timeRemaining = totalTimeSeconds;
        UpdateAccuracyUI();
        UpdateTimerUI();
    }

    private void ScheduleChainDisappear()
    {
        if (_activeSpawns.Count == 0 || chainDisappearCount <= 0)
        {
            return;
        }

        var indices = new List<int>();
        for (int i = 0; i < _activeSpawns.Count; i++)
        {
            indices.Add(i);
        }

        int toPick = Mathf.Min(chainDisappearCount, _activeSpawns.Count);
        for (int n = 0; n < toPick; n++)
        {
            if (indices.Count == 0) break;
            int randIdx = Random.Range(0, indices.Count);
            int pick = indices[randIdx];
            indices.RemoveAt(randIdx);

            var s = _activeSpawns[pick];
            float delay = Random.Range(chainMinDelay, chainMaxDelay);
            s.forcedExpireAt = s.age + Mathf.Max(0f, delay);
            _activeSpawns[pick] = s;
        }
    }

    private struct SpawnedData
    {
        public Transform transform;
        public Vector3 baseScale;
        public float age;
        public float lifetime;
        public float shrinkProgress;
        public bool shrinking;
        public float forcedExpireAt;
    }
}
