<div align="center">
  <h1>⚾ CatcherVR - VR 체감형 야구 시뮬레이션 게임</h1>
  <p><strong>플레이어가 직접 포수 및 타자가 되어 물리 엔진 기반의 정밀한 투구와 타구를 경험하는 극사실주의 VR 야구 게임</strong></p>

  <!-- 방패 뱃지들 -->
  <img src="https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white" alt="Unity">
  <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/OpenXR-E52222?style=for-the-badge" alt="OpenXR">
  <img src="https://img.shields.io/badge/Blender-F5792A?style=for-the-badge&logo=blender&logoColor=white" alt="Blender">
  <img src="https://img.shields.io/badge/AI_Used-10B981?style=for-the-badge" alt="AI 사용">
  <br><br>
</div>

## 📌 Project Overview
- **개발 기간:** 2026.08.04 ~ 2026.09.05 (약 1개월)
- **개발 인원:** 1인 개발 (VR 클라이언트 프로그래밍, 물리 시스템, AI 수비 로직, 3D 모델링 전담)
- **장르:** 1인칭 체감형 VR 스포츠 시뮬레이션
- **AI 활용 내역:** 게임 내 수비 및 투구 AI 기능 고도화 및 안정화 로직 검증에 일부 활용. 핵심 VR 상호작용과 물리 연산 시스템은 직접 설계 및 개발.

## 🎮 Game Concept
VR 헤드셋을 착용하고 1인칭 시점으로 그라운드에 서서 투수의 공을 직접 잡거나, 타자가 되어 배트로 쳐내는 체감형 야구 시뮬레이션입니다. `XR Interaction Toolkit`과 정교한 물리 연산을 결합하여 실제 야구장의 현장감을 구현했습니다.

### 💡 주요 특징 (Key Highlights)
1. **물리 기반 실시간 타구 궤적 및 낙구 지점 예측 시스템:** 
   `Rigidbody`의 선형 속도(`linearVelocity`)와 질량 기반 물리 연산(`BallProjectileCalculator`)을 통해 공의 궤적을 연산하고, 타격 순간 즉각적으로 정확한 낙구 지점(`_landPosition`)을 도출하여 `LineRenderer`로 시각화하는 시스템을 구축했습니다.
2. **FSM 및 역할 할당(Role Assignment) 기반의 유기적인 수비 AI:** 
   `BaseballPlayer` 베이스 클래스를 상속받은 수비수 객체들이 타구의 방향과 낙구 지점을 실시간으로 분석하여, 투수 베이스 커버 및 외야수 백업 등 실제 야구 전술에 맞춘 동적 방어 포지션(`AssignSpecialRole`)을 유기적으로 수행합니다.
3. **VR 정밀 상호작용 및 엄격한 야구 룰(Rule) 판정 시스템:** 
   `XR Interaction Toolkit`을 커스텀하여 글러브(`Glove`) 트랜스폼과 물리 충돌체의 상태를 연동했습니다. 노바운드 캐치 시 플라이 아웃 처리, 파울 라인 판독, 스트라이크 존 통과(`CountsProvider`) 등 복잡한 야구 규칙을 게임 로직에 정교하게 바인딩했습니다.

---

## 🛠 Tech Stack

### **핵심 환경 및 라이브러리**
- **Engine / Language:** Unity 3D / C#
- **VR & Input:** Unity XR Interaction Toolkit (XRI), Unity New Input System, OpenXR
- **3D Modeling:** Blender (배트, 공, 포수 마스크 등 핵심 오브젝트 직접 제작)
- **Animation:** DOTween
- **Architecture:** OOP 기반 상태 패턴(FSM) 및 이벤트 드리븐(Event-Driven) 아키텍처

---

## 🔥 Challenge & Solution

### 물리 엔진 사이클 충돌로 인한 궤적 계산 오류 및 수비 AI 동선 꼬임 현상 해결
**Problem:** 
배트에 공이 맞는 임팩트 순간(`OnCollisionEnter`), 투구 궤적을 계산하던 로직과 타구 궤적을 계산하는 로직이 물리 엔진 사이클 내에서 충돌하는 이슈가 발생했습니다. 이로 인해 공이 비정상적으로 감속하거나, 수비수 AI가 타구의 최종 낙구 지점(`_landPosition`)을 잘못 예측하여 수비 동선이 완전히 엉키는 현상이 있었습니다.

**Solution:** 
물리 엔진의 동기화 문제를 해결하기 위해 공(`Ball.cs`)의 상태 관리 라이프사이클을 전면 개편했습니다. 
공의 주체 상태를 플래그화(`_aiThrows`, `_playerThrows`, `_isBattedBall`)하여 충돌 판정 시 이벤트의 주도권을 명확히 분리했습니다. 특히 낙구 지점 예측 메서드(`predictBallPath`) 내부에 조건부 분기 처리를 직접 도입하여, '배트에 맞은 타구(`_isBattedBall`)'일 경우에만 공기 저항 감쇄 계수(`linearVelocity *= 0.35f`) 적용을 무시하도록 물리 공식을 보정했습니다. 

결과적으로 타구의 궤적이 외야까지 시원하게 자연스럽게 뻗어나가도록 개선되었고, 수비수 AI 또한 타구 방향(`_flyDirection`)을 정확히 룩업(Lookup)하여 완벽한 수비 위치로 이동하도록 물리 엔진의 엣지 케이스를 논리적으로 완벽히 극복해냈습니다.
