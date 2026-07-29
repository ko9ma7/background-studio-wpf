# Background Studio · C# WPF 1.2

Windows에서 이미지와 동영상 배경을 로컬로 제거하고 편집하는 .NET 10 WPF
앱입니다. 파일은 외부 AI API로 전송하지 않습니다.

## 기능

- U2NetP ONNX 모델을 앱에서 내려받고 MD5 체크섬 검증
- 이미지 배경 제거와 PNG/JPEG/BMP/TIFF/SVG 저장
- 단색·다른 이미지·원본 블러 배경 합성
- 마스크 기준과 가장자리 부드러움 조정
- 그림자 흐림·불투명도·오프셋
- 중앙 정렬·크기·X/Y 위치, 회전·반전, 캔버스 비율
- 밝기·대비·채도·색온도·색조·불투명도와 12종 필터
- 마스크 임계값·페더·확장/축소, 마스크·외곽선 단독 레이어
- SHA-256 검증을 거친 FFmpeg 8.1.2 앱 내부 자동 준비
- 투명 WebM/MOV 또는 MP4/WebM/MOV/GIF
- 여러 파일 대기열, 순차 변환, 취소, 재대기, 결과 기록
- 자동 결과 저장, 출력 폴더·선택 결과 열기, 모델/FFmpeg 상태

## Windows EXE

[GitHub Releases](https://github.com/ko9ma7/background-studio-wpf/releases)에서
`BackgroundStudio-WPF-v1.2.0-win-x64.zip`을 내려받아 압축을 풀고
`BackgroundStudio.exe`를 실행합니다. 이 배포본은 .NET 런타임을 포함하므로
.NET SDK나 FFmpeg를 따로 설치할 필요가 없습니다. ZIP 옆의 `.sha256` 파일로
다운로드 무결성을 확인할 수 있습니다.

## 소스 실행

소스에서 실행할 때는 Windows 10/11과 .NET 10 SDK가 필요합니다.

```powershell
dotnet restore BackgroundStudio.csproj
dotnet run --project BackgroundStudio.csproj
```

첫 화면의 `모델 준비`를 누르면 rembg가 배포하는 U2NetP 모델 약 5MB를
`%LOCALAPPDATA%\BackgroundStudio\models\`에 저장합니다.

동영상에서는 상단 `FFmpeg 준비`를 누르거나 바로 처리를 시작하면 검증된
Essentials ZIP을 `%LOCALAPPDATA%\BackgroundStudio\ffmpeg\`에 내려받습니다.
시스템 PATH 설정이나 관리자 권한은 필요하지 않습니다.

## 사용 순서

1. `이미지 추가` 또는 `동영상 추가`로 여러 파일을 대기열에 넣습니다.
2. 투명·단색·다른 이미지·블러 중 결과 배경 하나를 고릅니다.
3. 상단 탭에서 필터·외곽, 위치·크기, 고급 색·마스크 값을 조절합니다.
4. `대기열 전체 변환`을 누릅니다.
5. 완료 파일은 `사진\Background Studio`에 자동 저장되고 결과 목록에 남습니다.

저장 대화상자를 처리 도중 열지 않습니다. `저장·결과` 탭에서 출력 폴더를
바꾸고, 선택 결과 또는 폴더를 바로 열 수 있습니다. 새 작업은 `전체 초기화`,
잘못 넣은 항목은 `선택 삭제`로 정리합니다.

## 빌드와 테스트

```powershell
dotnet build BackgroundStudio.csproj --configuration Release
dotnet test BackgroundStudio.Tests.csproj --configuration Release
```

배포용 단일 EXE:

```powershell
dotnet publish BackgroundStudio.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true
```

## 현실적인 제한

- 복잡한 머리카락, 유리, 연기, 모션 블러는 수동 보정이 필요할 수 있습니다.
- 프레임별 배경 제거는 긴 동영상에서 느립니다. 먼저 짧은 구간으로 품질을
  확인하세요.
- FFmpeg는 AI가 아니라 영상 프레임 추출·인코딩 도구입니다. 앱이 자체 폴더에
  내려받으므로 시스템 PATH나 별도 수동 설치는 필요하지 않습니다.
- 투명 WebM은 재생 프로그램에 따라 알파가 검게 보일 수 있습니다.
- 이 앱은 Windows 전용입니다. 서버 자동화는 별도 Python 저장소를,
  설치 없는 사용은 별도 GitHub Pages 저장소를 사용하세요.

외부 구성요소는 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md), 디자인
판단은 [`docs/design-direction.md`](docs/design-direction.md)에 정리했습니다.
전문 편집값은 [`docs/pro-editing-guide.md`](docs/pro-editing-guide.md)를
확인하세요.

## License

앱 코드는 MIT입니다. 모델과 FFmpeg는 각자의 조건을 따릅니다.
