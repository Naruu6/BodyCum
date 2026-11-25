# BodyCum 1.0 설치 및 사용

[English README](../README.md) · [소스 빌드 안내](BUILD.md)

BodyCum은 **Koikatsu CharaStudio**의 **Status → Juices**에 캐릭터별 효과 조절 기능을 추가하는 BepInEx 플러그인입니다. 현재 플러그인 버전은 **1.0.0**, 어셈블리·파일 버전은 **1.0.0.0**입니다. Koikatsu Sunshine이나 본편용으로 안내하지 않습니다.

## 현재 배포 상태

현재는 소스 공개 준비 단계이며, **Releases에 완성된 `BodyCum.dll`은 아직 업로드되지 않았습니다.** 지금 사용하려면 필요한 로컬 리소스를 준비해 [빌드 안내](BUILD.md)에 따라 직접 빌드해야 합니다.

셰이더 번들 4개와 편집한 `AccessoryJuiceMask.png`는 출처 확인이 진행 중이므로 저장소에서 제외했습니다. 저장소를 내려받는 것만으로 바로 빌드되지는 않습니다. 관련 정보는 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)에 정리합니다.

## 준비 사항

- Windows용 Koikatsu CharaStudio와 **Direct3D 11** 렌더링 환경
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases)
- [KKAPI 1.42.1 이상](https://github.com/IllusionMods/IllusionModdingAPI/releases)
- [KK BepisPlugins](https://github.com/IllusionMods/BepisPlugins/releases)에 포함된 Extended Save (`ExtensibleSaveFormat.dll`)
- F1 설정 창을 사용하기 위한 [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)

의존성은 **KK용** 패키지를 선택해 게임 폴더에 설치합니다. KKS·AI·HS2 등 다른 게임용 패키지를 함께 넣지 마세요. 이미 필요한 버전이 설치되어 있다면 중복으로 설치할 필요가 없습니다.

## DLL 설치 또는 교체

직접 빌드한 DLL을 준비한 뒤 다음 순서로 진행합니다. 나중에 Releases에 DLL이 제공되면 같은 방법으로 설치할 수 있습니다.

1. CharaStudio를 종료합니다.
2. 기존 `BodyCum.dll`이 있다면 `BepInEx/plugins` **밖에** 백업합니다. 백업 DLL을 plugins 안에 두면 중복으로 로드될 수 있습니다.
3. 새 `BodyCum.dll`을 게임 폴더의 `BepInEx/plugins`에 넣습니다. 기존 파일을 교체하고 BodyCum DLL은 한 개만 둡니다.
4. CharaStudio를 다시 실행하고 캐릭터를 선택합니다.
5. **Status → Juices**를 엽니다.

셰이더와 마스크 이미지는 빌드된 DLL에 들어 있습니다. 별도 이미지나 텍스처를 게임 폴더에 설치할 필요가 없습니다.

### 기존 SkinEffects와 함께 사용할 때

순정 [SkinEffects](https://github.com/ManlyMarco/Illusion_SkinEffects)는 별도 플러그인으로 함께 사용할 수 있습니다. BodyCum 기능이 이미 들어 있는 수정판 `KK_SkinEffects.dll`과는 함께 사용하지 않습니다. 이 경우 BodyCum은 경고를 남기고 비활성화됩니다.

수정판에서 옮긴다면 게임을 종료하고 `KK_SkinEffects.dll`과 `BepInEx/config/Marco.SkinEffects.cfg`를 백업한 다음, 수정판을 제거하거나 순정 SkinEffects로 교체하고 BodyCum을 설치합니다.

## 기본 사용법

**BodyCum ON/OFF**는 현재 선택한 캐릭터에 적용됩니다. 녹색 ON은 설정한 효과를 표시하고, 회색 OFF는 게임 기본 Juices 표시로 돌아갑니다. 캐릭터별 설정은 씬에 저장됩니다.

**F1 → BodyCum → General → Enable BodyCum**은 전체 표시를 켜고 끕니다. 이 값을 바꿔도 각 캐릭터의 스위치와 설정은 유지됩니다. F1 창이 열리지 않는다면 ConfigurationManager 설치와 해당 플러그인의 단축키 설정을 확인합니다.

| 조절 항목 | 동작 |
| --- | --- |
| Juice color | 색상 팔레트 열기 |
| Opacity | 불투명도 0~100% |
| Density | 농도 0~100% |
| Brightness | 밝기 0~200% |
| Belly / Tongue | OFF / x1 / x2 |
| Accessory / Clothes | OFF / x1 / x2와 슬롯 선택 |
| Acc. X/Y / Cloth X/Y | 위치 −100~100 |
| Acc. Size / Cloth Size | 크기 20~250% |

**All slots**는 현재 캐릭터의 장착된 액세서리와 의상 슬롯을 함께 선택하거나 해제합니다. All ON은 OFF인 종류를 x1로 켜며, 이미 x2인 경우에는 x2를 유지합니다. All OFF는 슬롯 선택만 해제하므로 위치·크기·단계·표면 배치 기준은 유지됩니다. 이 버튼은 의상이나 액세서리 자체의 표시 상태를 바꾸지 않습니다.

혀 효과는 현재 보이는 게임 기본 혀 메시를 사용합니다. 혀가 보이는 표정과 포즈는 Studio에서 따로 선택해야 합니다.

Custom, Gloss, Use defaults, Reapply 버튼은 없습니다. BodyCum이 켜져 있을 때는 기존 Custom ON·Gloss OFF 동작을 사용합니다. 표면에 고정된 배치는 움직임을 따라가며, X/Y·Size는 계속 조절할 수 있습니다.

설정 파일은 `BepInEx/config/koikatsu.bodycum.cfg`입니다. 처음 설정하는 항목은 이전 `Marco.SkinEffects.cfg`의 `[Custom Juice Color]` 값을 가져올 수 있습니다. 기존 씬의 색상과 다른 설정 키는 유지하지만, 제거된 Custom·Gloss 플래그는 더 이상 적용하지 않습니다.

## 확인된 범위와 제한

몸과 얼굴 효과는 표준 피부 UV를 기준으로 합니다. 비표준 UV, 셰이더가 변형하는 메시, 특수한 투명도 처리는 실제 게임에서 확인해야 합니다. 정면 XY 투영 때문에 옆을 향한 액세서리 표면에서는 무늬가 눌려 보일 수 있습니다. 고폴리 메시를 여러 캐릭터에 동시에 적용하면 CPU 사용량이 늘 수 있습니다.

기존 1.0.0 최종 기준본은 로컬 컴파일과 정적 호환성 검사를 통과했습니다. **그 최종 기준본을 실제 Studio에서 실행해 UI·렌더링·씬 저장 및 재로드 전체를 확인하는 검사는 수행하지 않았습니다.**

## 제거 또는 복구

CharaStudio를 종료하고 `BodyCum.dll`을 plugins 폴더 밖으로 옮기면 플러그인을 제거할 수 있습니다. 이전 BodyCum으로 되돌리려면 새 DLL을 제거하고 백업한 DLL 한 개를 `BodyCum.dll`로 복원합니다. 이전 통합 SkinEffects 수정판으로 돌아갈 경우에는 BodyCum을 제거한 뒤 해당 백업을 복원합니다.
