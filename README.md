# FolderGraph

폴더와 파일의 트리 구조를 **3D 공간의 인터랙티브 그래프**로 시각화하는 Windows 데스크톱 앱입니다.
폴더는 노드(구체)로, 부모-자식 관계는 간선으로 표현되며, 힘-기반(force-directed) 시뮬레이션으로
서로 밀고 당기며 자연스럽게 자리를 잡습니다. CAD 스타일 카메라로 그래프를 자유롭게 회전·이동·확대하며
탐색할 수 있습니다.

---
<img width="1920" height="1040" alt="Image" src="https://github.com/user-attachments/assets/84ab17bb-139f-4dea-aeba-9ac0e17e94e2" />

## 주요 기능

### 시각화
- **3D 그래프 렌더링** — 폴더/파일을 구체로, 계층 관계를 간선으로 3차원 공간에 표시
- **힘-기반 배치** — 연결된 노드는 가까이, 무관한 노드는 사방으로 밀어내며 군집을 형성
- **깊이별 노드 크기** — 최상위가 가장 크고, 자식으로 갈수록 5단계로 작아짐
- **깊이별 자동 색상** — 불러오는 즉시 최상위 폴더마다 다른 색이 부여되고, 자식은 같은 색 계열로
  깊이에 따라 옅어지는 명도가 적용됨
- **불러오는 즉시 정돈** — 노드가 중앙에 모인 정돈된 배치로 시작하고, 카메라가 자동으로 중앙을 비춤

### 카메라 (CAD 스타일)
| 조작 | 동작 |
|------|------|
| 빈 공간 **좌클릭 드래그** | 회전(궤도) |
| **휠 버튼 드래그** | 평행 이동(팬) |
| **휠 스크롤** | 확대/축소(줌) |



### 상호작용
- **노드 클릭** — 선택. 선택한 노드와 모든 하위 자손이 강조되고 나머지는 어두워짐
- **노드 좌클릭 드래그** — 노드 이동. 주변 노드가 따라 반응(선택된 노드를 끌면 그 노드만 이동)
- **호버** — 노드 위에 마우스를 올리면 이름 툴팁 표시
- **더블클릭** — 파일을 OS 기본 앱으로 열기
- **우클릭** — 색상 팔레트로 노드(및 자손) 색칠 / 회색으로 초기화
- **폴더로 드롭** — 노드를 폴더 위에 끌어다 놓으면 실제 파일/폴더 이동(확인 후)

### 도구
- **깊이 슬라이더** — 표시할 폴더 계층 깊이 조절. 변경 시 색을 유지한 채 중앙으로 다시 정돈
- **검색** — 이름으로 노드 검색, 일치 항목을 하늘색으로 강조
- **숨김 표시** — 숨김 파일/폴더 포함 여부 토글
- **색상별 노드 집계** — 좌상단에 색상별 노드 수와 전체 대비 비율 표시(색을 바꿀 때마다 갱신).
  라벨은 그 색을 칠한 최상위 노드 이름으로 표시(여럿이면 "이름 외 N")
- **PNG 내보내기** — 현재 보이는 3D 화면을 이미지 파일로 저장

---

## 기술 스택

- **언어/런타임**: C# 7.3, .NET Framework 4.7.2
- **UI**: WPF (Windows Presentation Foundation), `Viewport3D` 기반 3D 렌더링
- **3D 수학**: `System.Windows.Media.Media3D` (외부 3D 라이브러리 없음)
- **UI 프레임워크**: MahApps.Metro
- **아키텍처**: MVVM 패턴, 생성자 주입 기반의 단순 의존성 조립

---

## 빌드 및 실행

### 요구 사항
- Windows
- Visual Studio 2017 이상
- .NET Framework 4.7.2 개발 도구

### 실행
1. Visual Studio에서 솔루션을 엽니다.
2. NuGet 패키지(MahApps.Metro)를 복원합니다.
3. 빌드 후 실행합니다.
4. 상단 입력란에 폴더 경로를 넣고 **불러오기**를 누릅니다.

---

## 프로젝트 구조

```
FolderGraph/
├─ App.xaml(.cs)              앱 진입점 + 의존성 조립
├─ MainWindow.xaml(.cs)       메인 창(툴바 + 3D 뷰 + 상태바)
│
├─ Core/                      MVVM 기반 클래스
│   ├─ ObservableObject.cs        INotifyPropertyChanged 베이스
│   ├─ RelayCommand.cs            ICommand 구현
│   └─ BulkObservableCollection.cs 대량 갱신 시 알림 1회로 묶는 컬렉션
│
├─ Models/                    데이터 모델
│   ├─ FileNodeModel.cs           파일/폴더 노드
│   ├─ NodeType.cs                Folder / File
│   └─ GraphData.cs               스캔 결과
│
├─ Services/                  파일시스템·다이얼로그 서비스 (+ Abstractions/)
│   ├─ FolderScanner.cs           폴더 비동기 스캔(BFS)
│   ├─ FileOperationService.cs    파일 이동 / 기본 앱 열기
│   └─ DialogService.cs           확인 / 에러 팝업
│
├─ Graph/                     배치·물리 (+ Abstractions/)
│   ├─ SimpleGridLayout.cs        초기 시드 격자 배치
│   ├─ ForceDirectedSimulation.cs 힘-기반 시뮬레이션(3축)
│   └─ GridForceSimulation.cs     공간 격자로 가속한 대용량용 시뮬레이션(선택)
│
├─ Helpers/                   상수·색상 계산 (+ Abstractions/)
│   ├─ AppConstants.cs            노드 수 상한, 크기/색 단계 등 튜닝 상수
│   └─ DepthColorCalculator.cs    깊이별 명도 그라데이션 계산
│
├─ ViewModels/                MVVM의 VM
│   ├─ MainViewModel.cs           전체 상태·명령·시뮬레이션 제어
│   ├─ NodeViewModel.cs           노드(좌표/색/상태, IPhysicsBody 구현)
│   ├─ EdgeViewModel.cs           간선
│   ├─ PaletteColor.cs            팔레트 색
│   └─ ColorStat.cs               색상별 집계 항목
│
├─ View3D/                    3D 뷰
│   ├─ Graph3DView.xaml(.cs)      Viewport3D 렌더링·동기화·상호작용
│   ├─ OrbitCameraController.cs   CAD 궤도 카메라(회전/팬/줌, 화면→평면 변환)
│   └─ SphereMeshFactory.cs       재사용 단위 구 메시
│
├─ Views/                     보조 뷰
│   └─ ToolbarView.xaml(.cs)      상단 툴바
│
└─ Converters/                값 변환기
```

---

## 동작 원리

### 렌더링
단위 구 메시 하나를 만들어 모든 노드가 공유하고, 각 노드는 크기·위치 변환만 다르게 적용합니다.
매 프레임 노드 위치와 색, 간선 메시를 동기화하되, 위치가 바뀐 경우에만 간선을 다시 만들어
유휴 부하를 줄입니다.

### 카메라
궤도 카메라는 구 좌표(거리/방위각/고도각)로 관리되어 항상 대상(target)을 바라봅니다.
노드를 끌 때는 화면 좌표를 카메라 정면 평면 위의 3D 지점으로 역투영(unprojection)해
마우스를 정확히 따라가도록 합니다.

### 상호작용(레이캐스팅)
마우스 위치에서 광선을 쏘아(`VisualTreeHelper.HitTest`) 맞은 구체를 찾고, 모델→노드 역매핑으로
어떤 노드인지 판별합니다. 선택·호버·드롭 대상 강조는 발광(Emissive) 머티리얼로 표현합니다.

### 색상 그라데이션
색칠한 노드를 루트로 삼아, 자손으로 내려갈수록 흰색과 섞어 옅어지는 명도를 적용합니다(깊이 0~4,
5단계). 깊이를 늘려 새로 나타난 자식에는 부모 색 계열을 이어 칠합니다.

### 성능
- 노드/간선을 한 번에 일괄 등록해, 수천 개 노드를 채울 때 뷰가 매번 전체 재빌드하는 것을 방지
- 불러오는 시점에 시뮬레이션을 미리 일부 수렴(prewarm)시켜 정돈된 상태로 표시
- 매우 큰 그래프에서는 `App.xaml.cs`에서 시뮬레이션을 `GridForceSimulation`으로 교체하면
  반발력 계산이 공간 격자로 가속됨

---

## 설정값 (Helpers/AppConstants.cs)

| 상수 | 기본값 | 설명 |
|------|--------|------|
| `MaxNodes` | 2000 | 렌더링할 최대 노드 수(초과 시 스캔 중단) |
| `MaxColorDepth` | 4 | 색 명도 그라데이션 단계(0~4) |
| `MaxSizeDepth` | 4 | 노드 크기 단계(0~4) |
| `MaxNodeRadius` / `MinNodeRadius` | 22 / 9 | 최상위/최하위 노드 반지름 |
| `MinDepth` / `MaxDepth` | 1 / 10 | 깊이 슬라이더 범위 |
