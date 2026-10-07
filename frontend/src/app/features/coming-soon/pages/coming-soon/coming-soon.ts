import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { comingSoonModuleName } from '../../../../core/config/coming-soon-modules';
import { comingSoonTarget } from '../../coming-soon.routes';

/** FR-03: placeholder for a future module. Static: no request, no data. */
@Component({
  selector: 'app-coming-soon',
  imports: [RouterLink, ButtonDirective],
  templateUrl: './coming-soon.html',
  styleUrl: './coming-soon.scss',
})
export class ComingSoon {
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap);

  readonly moduleName = computed(
    () => comingSoonModuleName(this.params()?.get('module') ?? '') ?? '',
  );
  readonly target = comingSoonTarget(this.route.snapshot);
}
