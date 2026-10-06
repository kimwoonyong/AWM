using AWM.Models;

namespace AWM.Services.Interfaces;

public interface IImageShrinker
{
    /// <summary>
    /// Claude 에게 보여 줄 사본을 만든다(긴 변 1568px 이하, 회전 적용). 원본 바이트는 바꾸지 않는다 (D-013).
    /// 읽을 수 없는 그림이면 <see cref="NotSupportedException"/>.
    /// </summary>
    ImageInput Shrink(string fileName, byte[] original);
}
