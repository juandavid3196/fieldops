import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { TranslocoPipe } from '@jsverse/transloco';

import { PermissionMatrix as PermissionMatrixData } from '../../models/users.model';

import { t } from '../../../../core/i18n/translate';
import { CodeLabelPipe } from '../../../../core/i18n/code-label.pipe';
export type LevelTone = 'primary' | 'info' | 'warning' | 'neutral';

export const MATRIX_ERROR_MESSAGE = (): string => t('users.permissionMatrix.matrixErrorMessage');

/** BR-07 tones: Full/Edit teal, View info, None neutral, every scoped level warning. */
export function levelTone(level: string): LevelTone {
  switch (level) {
    case 'full':
    case 'edit':
      return 'primary';
    case 'view':
      return 'info';
    case 'none':
      return 'neutral';
    default:
      return 'warning';
  }
}

/** Read-only permission matrix and legend; renders exactly the API catalog (FR-05). */
@Component({
  selector: 'app-permission-matrix',
  imports: [CodeLabelPipe, TranslocoPipe, ButtonDirective, Message, Skeleton],
  templateUrl: './permission-matrix.html',
  styleUrl: './permission-matrix.scss',
})
export class PermissionMatrix {
  readonly matrix = input<PermissionMatrixData | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly retry = output<void>();

  get errorMessage(): string {
    return MATRIX_ERROR_MESSAGE();
  }
  readonly tone = levelTone;
}
