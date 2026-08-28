using UnityEngine;

public class Bat : MonoBehaviour
{
    private Vector3 _prevPosition;
    private Quaternion _prevRotation;
    private Vector3 _batVelocity;
    
    // 배트 규격 (CapsuleCollider 기준)
    private const float SWEET_SPOT_Y = 0.55f; // 배트 그립으로부터 약 55cm 지점 (스위트 스폿)
    private const float BAT_RADIUS = 0.034f; // 배트 배럴 반지름

    void Start()
    {
        _prevPosition = transform.position;
        _prevRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        // 실시간 배트 스윙 속도 추적
        float dt = Time.fixedDeltaTime;
        if (dt > 0.0001f)
        {
            _batVelocity = (transform.position - _prevPosition) / dt;
        }

        _prevPosition = transform.position;
        _prevRotation = transform.rotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        ProcessBatHit(collision);
    }

    public void ProcessBatHit(Collision collision)
    {
        if (!collision.gameObject.name.ToLower().Contains("ball") && !collision.gameObject.CompareTag("Ball")) return;

        Ball ball = collision.gameObject.GetComponent<Ball>();
        if (ball == null) return;

        Rigidbody ballRig = ball.GetComponent<Rigidbody>();
        if (ballRig == null) return;

        ContactPoint contact = collision.GetContact(0);
        Vector3 hitPoint = contact.point;
        Vector3 hitNormal = contact.normal;

        // 1. 배트 로컬 좌표계로 충돌 지점 변환 (Y축: 손잡이 0.0 ~ 배트 끝 0.85)
        Vector3 localHit = transform.InverseTransformPoint(hitPoint);
        float hitY = localHit.y;

        // 2. 스위트 스폿(길이 방향) 반발 효율 계산 (0.35 ~ 1.0)
        // 0.55m 부근이 1.0(스위트스폿), 0.75m 이상은 0.65(배트 끝 빗맞음), 0.35m 이하는 0.4(손잡이 먹힌 타구)
        float distFromSweetSpot = Mathf.Abs(hitY - SWEET_SPOT_Y);
        float sweetSpotEfficiency = Mathf.Clamp01(1.0f - (distFromSweetSpot / 0.35f));
        float lengthPowerFactor = Mathf.Lerp(0.40f, 1.0f, sweetSpotEfficiency);

        // 3. 단면(두께 방향) 오프셋 계산 (정타 품질 0.0 ~ 1.0)
        Vector2 crossSectionOffset = new Vector2(localHit.x, localHit.z);
        float offsetFromCenter = crossSectionOffset.magnitude;
        float centerQuality = Mathf.Clamp01(1.0f - (offsetFromCenter / BAT_RADIUS));

        // 4. 배트 스윙 속도와 투구 속도 결합 (Exit Velocity Equation)
        Vector3 incomingPitchVel = ballRig.linearVelocity;
        float batSpeedMagnitude = _batVelocity.magnitude;
        
        // VR 스윙 속도가 측정되지 않거나 느릴 경우 기본 스윙 속도 보정 (약 25m/s = 90km/h)
        if (batSpeedMagnitude < 10f)
        {
            batSpeedMagnitude = Random.Range(24f, 32f);
        }

        float pitchSpeed = incomingPitchVel.magnitude;
        // 타구 속도 = 0.18 * 투구속도 + 1.15 * 배트속도 * 길이효율 * 단면정타품질
        float exitSpeed = (0.18f * pitchSpeed + 1.15f * batSpeedMagnitude) * lengthPowerFactor * (0.6f + 0.4f * centerQuality);
        
        // 현실적 타구 속도 범위 제한: 먹힌 타구 14m/s(50km/h) ~ 강타 46m/s(165km/h)
        exitSpeed = Mathf.Clamp(exitSpeed, 14f, 46f);

        // 5. 발사각(Launch Angle) 계산
        // 배트 중심선보다 위쪽에 맞으면 땅볼(Ground ball), 아래쪽에 맞으면 플라이(Fly ball), 정중앙이면 라인드라이브(Line drive)
        float verticalOffset = Vector3.Dot(hitPoint - transform.position, Vector3.up);
        float launchAngleDeg = 15f;

        if (verticalOffset > 0.012f) // 윗부분 맞음 -> 땅볼
        {
            launchAngleDeg = Random.Range(-10f, 8f);
        }
        else if (verticalOffset < -0.012f) // 아랫부분 맞음 -> 플라이볼
        {
            launchAngleDeg = Random.Range(28f, 52f);
        }
        else // 정타 (Center) -> 이상적인 12~22도 라인드라이브
        {
            launchAngleDeg = Random.Range(12f, 22f);
        }

        // 빗맞은 타구(손잡이 부근 또는 배트 끝)의 불규칙 바운드 및 팝플라이
        if (sweetSpotEfficiency < 0.35f)
        {
            if (Random.value > 0.5f) launchAngleDeg = Random.Range(55f, 75f); // 내야 팝플라이
            else launchAngleDeg = Random.Range(-15f, 0f); // 힘없는 내야 땅볼
            exitSpeed *= 0.65f; // 반발력 대폭 감소
        }

        // 6. 좌우 발사 방향 (외야 필드 방향: 홈에서 외야로 Z-)
        Vector3 fieldForward = Vector3.back; // Z- 방향이 외야
        
        // 타격 타이밍 및 배트 접촉면 법선에 따른 좌우 분산
        Vector3 normalXZ = new Vector3(hitNormal.x, 0, hitNormal.z).normalized;
        Vector3 hitDirection = Vector3.Slerp(fieldForward, normalXZ, 0.4f).normalized;

        // 파울 판정 (좌우 45도 초과 시 파울)
        float angleFromCenter = Vector3.Angle(Vector3.back, hitDirection);
        bool isFoul = (angleFromCenter > 45f) || (centerQuality < 0.2f && Random.value > 0.5f);

        // 7. 최종 3D 타구 속도 벡터 생성 및 적용
        float launchRad = launchAngleDeg * Mathf.Deg2Rad;
        Vector3 finalVelocity = (hitDirection * Mathf.Cos(launchRad) + Vector3.up * Mathf.Sin(launchRad)) * exitSpeed;

        ballRig.linearVelocity = finalVelocity;
        ball.SetBattedBall(true);
        ball.IsFoul = isFoul;
        ball.LostBall();

        if (GamePlayerProvider.provider != null)
        {
            GamePlayerProvider.provider.NotifyBatterHit();
        }
    }
}
