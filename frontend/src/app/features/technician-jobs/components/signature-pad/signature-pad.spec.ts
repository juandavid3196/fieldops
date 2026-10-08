import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SIGNATURE_LOST_MESSAGE, SignaturePad } from './signature-pad';

describe('SignaturePad', () => {
  let fixture: ComponentFixture<SignaturePad>;
  let root: HTMLElement;

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => (b.textContent ?? '').includes(label))!;
  const stroke = async (from: number, to: number) => {
    const canvas = root.querySelector('canvas')!;
    const pointer = (type: string, x: number) =>
      canvas.dispatchEvent(
        Object.assign(new MouseEvent(type, { clientX: x, clientY: 3, bubbles: true }), {
          pointerId: 1,
        }),
      );
    pointer('pointerdown', from);
    pointer('pointermove', to);
    pointer('pointerup', to);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  function create(context: object | null): void {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      context as unknown as CanvasRenderingContext2D,
    );
    fixture = TestBed.createComponent(SignaturePad);
    root = fixture.nativeElement;
    fixture.detectChanges();
  }

  afterEach(() => vi.restoreAllMocks());

  it('tracks strokes for status, Clear and Redo, exports a PNG and reports a lost signature (BR-15)', async () => {
    const context = {
      fillRect: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
      arc: vi.fn(),
      fill: vi.fn(),
    };
    create(context);
    const canvas = root.querySelector('canvas')!;
    expect(canvas.getAttribute('aria-label')).toBe('Customer signature');
    expect(text()).toContain('No signature');
    expect(button('Clear').disabled).toBe(true);
    expect(button('Redo').disabled).toBe(true);

    await stroke(0, 5);
    await stroke(6, 9);
    expect(fixture.componentInstance.strokes().length).toBe(2);
    expect(text()).toContain('Signature captured');
    expect(button('Clear').disabled).toBe(false);

    button('Redo').click();
    fixture.detectChanges();
    expect(fixture.componentInstance.strokes().length).toBe(1);
    expect(text()).toContain('Signature captured');

    button('Redo').click();
    fixture.detectChanges();
    expect(text()).toContain('No signature');
    expect(button('Redo').disabled).toBe(true);

    await stroke(1, 2);
    button('Clear').click();
    fixture.detectChanges();
    expect(fixture.componentInstance.strokes()).toEqual([]);

    const toBlob = vi
      .spyOn(HTMLCanvasElement.prototype, 'toBlob')
      .mockImplementation((callback) => callback(new Blob(['png'], { type: 'image/png' })));
    const png = await fixture.componentInstance.exportPng();
    expect(png?.type).toBe('image/png');
    expect(toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/png');

    // A redraw that is impossible clears the signature and says so.
    vi.mocked(HTMLCanvasElement.prototype.getContext).mockReturnValue(null);
    fixture.componentInstance.strokes.set([[[0.1, 0.1]]]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.strokes()).toEqual([]);
    expect(text()).toContain(SIGNATURE_LOST_MESSAGE);
  });
});
