import { Injectable, Injector, inject } from '@angular/core';
import { ActivatedRouteSnapshot, BaseRouteReuseStrategy, Router } from '@angular/router';

/** Navigation `state` flag: a same-route navigation with other path params rebuilds the page. */
export const RELOAD_ON_PARAM_CHANGE = 'reloadOnParamChange';

/**
 * Detail pages read their path params once, so going from `/quotes/a` to `/quotes/b` would keep
 * showing `a`. Only navigations that opt in with {@link RELOAD_ON_PARAM_CHANGE} recreate the page.
 */
@Injectable()
export class ReloadRouteReuseStrategy extends BaseRouteReuseStrategy {
  // The router itself depends on this strategy, so it is resolved lazily.
  private readonly injector = inject(Injector);

  override shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    if (!super.shouldReuseRoute(future, curr)) {
      return false;
    }
    const state = this.injector.get(Router).getCurrentNavigation()?.extras.state;
    if (state?.[RELOAD_ON_PARAM_CHANGE] !== true) {
      return true;
    }
    const keys = new Set([...Object.keys(future.params), ...Object.keys(curr.params)]);
    return [...keys].every((key) => future.params[key] === curr.params[key]);
  }
}
