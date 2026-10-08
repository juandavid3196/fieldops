import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { ButtonDirective } from 'primeng/button';

/** Point normalized to the drawing area (0..1), so a resize redraws the same signature. */
export type SignaturePoint = readonly [number, number];
export type SignatureStroke = readonly SignaturePoint[];

export const SIGNATURE_CAPTURED = 'Signature captured';
export const SIGNATURE_EMPTY = 'No signature';
export const SIGNATURE_LOST_MESSAGE = 'Signature cleared. Ask the customer to sign again.';

const LINE_WIDTH = 2.5;
let nextId = 0;

/** BR-15 signature pad: native canvas with Pointer Events (touch, pen, mouse). */
@Component({
  selector: 'app-signature-pad',
  imports: [ButtonDirective],
  templateUrl: './signature-pad.html',
  styleUrl: './signature-pad.scss',
})
export class SignaturePad {
  private readonly destroyRef = inject(DestroyRef);
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private current: SignaturePoint[] | null = null;
  private activePointer: number | null = null;

  /** Strokes of the signature; owned by the page so it survives leaving and re-entering the step. */
  readonly strokes = model<readonly SignatureStroke[]>([]);
  readonly disabled = input(false);
  readonly invalid = input(false);
  /** Id of the field error linked to the drawing area. */
  readonly errorId = input<string | null>(null);

  readonly statusId = `signature-status-${nextId++}`;
  readonly captured = computed(() => this.strokes().length > 0);
  readonly statusText = computed(() => (this.captured() ? SIGNATURE_CAPTURED : SIGNATURE_EMPTY));
  readonly describedBy = computed(() => [this.statusId, this.errorId()].filter(Boolean).join(' '));
  /** Polite announcement when a signature could not be kept. */
  readonly notice = signal('');

  constructor() {
    afterNextRender(() => {
      this.redraw();
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() => this.redraw());
      const element = this.canvas()?.nativeElement;
      if (element !== undefined) {
        observer.observe(element);
      }
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
    effect(() => {
      this.strokes();
      untracked(() => this.redraw());
    });
  }

  focus(): void {
    this.canvas()?.nativeElement.focus();
  }

  /** The only place that touches `canvas.toBlob`; resolves `null` when no PNG can be produced. */
  exportPng(): Promise<Blob | null> {
    const element = this.canvas()?.nativeElement;
    if (element === undefined) {
      return Promise.resolve(null);
    }
    return new Promise((resolve) => element.toBlob((blob) => resolve(blob), 'image/png'));
  }

  clear(): void {
    this.strokes.set([]);
    this.focus();
  }

  /** Redo removes the last stroke. */
  undo(): void {
    this.strokes.update((strokes) => strokes.slice(0, -1));
    this.focus();
  }

  start(event: PointerEvent): void {
    if (this.disabled() || this.activePointer !== null) {
      return;
    }
    event.preventDefault();
    const element = this.canvas()?.nativeElement;
    this.activePointer = event.pointerId;
    element?.setPointerCapture?.(event.pointerId);
    element?.focus();
    this.current = [this.pointOf(event)];
    this.notice.set('');
    this.drawStroke(this.current);
  }

  move(event: PointerEvent): void {
    if (this.current === null || event.pointerId !== this.activePointer) {
      return;
    }
    this.current.push(this.pointOf(event));
    this.drawStroke(this.current.slice(-2));
  }

  end(event: PointerEvent): void {
    if (this.current === null || event.pointerId !== this.activePointer) {
      return;
    }
    const stroke = this.current;
    this.current = null;
    this.activePointer = null;
    this.strokes.update((strokes) => [...strokes, stroke]);
  }

  private pointOf(event: PointerEvent): SignaturePoint {
    const rect = this.canvas()?.nativeElement.getBoundingClientRect();
    const clamp = (value: number) => Math.min(1, Math.max(0, value));
    return [
      clamp((event.clientX - (rect?.left ?? 0)) / (rect?.width || 1)),
      clamp((event.clientY - (rect?.top ?? 0)) / (rect?.height || 1)),
    ];
  }

  private context(): CanvasRenderingContext2D | null {
    return this.canvas()?.nativeElement.getContext?.('2d') ?? null;
  }

  /** Fits the bitmap to the displayed size, then repaints every stroke from its normalized points. */
  private redraw(): void {
    const element = this.canvas()?.nativeElement;
    if (element === undefined) {
      return;
    }
    const scale = globalThis.devicePixelRatio || 1;
    const rect = element.getBoundingClientRect();
    const width = Math.round(rect.width * scale);
    const height = Math.round(rect.height * scale);
    if (width > 0 && height > 0 && (element.width !== width || element.height !== height)) {
      element.width = width;
      element.height = height;
    }
    const context = this.context();
    if (context === null) {
      // Redraw is impossible: the signature cannot be kept (BR-15).
      if (this.captured()) {
        this.strokes.set([]);
        this.notice.set(SIGNATURE_LOST_MESSAGE);
      }
      return;
    }
    context.fillStyle = '#fff';
    context.fillRect(0, 0, element.width, element.height);
    this.strokes().forEach((stroke) => this.drawStroke(stroke, context));
  }

  private drawStroke(points: readonly SignaturePoint[], context = this.context()): void {
    const element = this.canvas()?.nativeElement;
    if (context === null || element === undefined || points.length === 0) {
      return;
    }
    const scale = globalThis.devicePixelRatio || 1;
    const at = ([x, y]: SignaturePoint): [number, number] => [
      x * element.width,
      y * element.height,
    ];
    context.strokeStyle = '#102a43';
    context.fillStyle = '#102a43';
    context.lineWidth = LINE_WIDTH * scale;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    const [startX, startY] = at(points[0]);
    if (points.length === 1) {
      context.beginPath();
      context.arc(startX, startY, (LINE_WIDTH * scale) / 2, 0, Math.PI * 2);
      context.fill();
      return;
    }
    context.beginPath();
    context.moveTo(startX, startY);
    points.slice(1).forEach((point) => context.lineTo(...at(point)));
    context.stroke();
  }
}
