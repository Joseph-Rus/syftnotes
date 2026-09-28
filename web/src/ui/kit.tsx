import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { back, href, type Route } from '../router';
import { ChevronLeft, ChevronRight } from './icons';
import { PULL_REST, Pull, progress } from './pull';

// The pieces every screen is built from: a navigation bar with a large title that shrinks into it, a scrolling body
// that pulls to refresh, inset grouped lists, a segmented control, and the empty and loading states.

export interface ScreenProps {
  title: string;
  /** A line under the large title ("12 lectures"). */
  subtitle?: ComponentChildren;
  /** Where Back goes when there's no screen before this one in the history; no back button without it. */
  back?: { label: string; to: Route };
  /** Buttons on the right of the bar. */
  actions?: ComponentChildren;
  /** Called by pulling down; the spinner stays until it resolves. */
  onRefresh?: () => Promise<unknown>;
  /** A large title (the tab's first screens); detail screens show the title in the bar only. */
  large?: boolean;
  /** Something that stays under the bar (a search field, a segmented control). */
  accessory?: ComponentChildren;
  children: ComponentChildren;
  class?: string;
}

export function Screen(props: ScreenProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const sentinel = useRef<HTMLDivElement>(null);
  const [condensed, setCondensed] = useState(!props.large);
  const [pull, setPull] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  const tracker = useRef(new Pull());

  useEffect(() => {
    if (!props.large || !sentinel.current || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver(([e]) => setCondensed(!e!.isIntersecting), {
      root: scroller.current,
      threshold: 0,
    });
    io.observe(sentinel.current);
    return () => io.disconnect();
  }, [props.large]);

  const touch = props.onRefresh
    ? {
        onTouchStart: (e: TouchEvent) => {
          if (!refreshing) tracker.current.start(e.touches[0]!.clientY, scroller.current?.scrollTop ?? 0);
        },
        onTouchMove: (e: TouchEvent) => {
          if (refreshing) return;
          const d = tracker.current.move(e.touches[0]!.clientY, scroller.current?.scrollTop ?? 0);
          setPull(d);
        },
        onTouchEnd: async () => {
          if (refreshing) return;
          if (tracker.current.end()) {
            setRefreshing(true);
            setPull(PULL_REST);
            navigator.vibrate?.(8);
            try {
              await props.onRefresh!();
            } finally {
              setRefreshing(false);
              setPull(0);
            }
          } else setPull(0);
        },
      }
    : {};

  const shift = pull > 0 ? { transform: `translateY(${pull}px)` } : undefined;
  return (
    <section class={`screen ${props.class ?? ''}`}>
      <header class={`navbar${condensed ? ' condensed' : ''}`}>
        <div class="navbar-row">
          <div class="navbar-side left">{props.back ? <BackButton {...props.back} /> : null}</div>
          <h1 class="navbar-title" aria-hidden={props.large && !condensed ? 'true' : undefined}>
            {props.title}
          </h1>
          <div class="navbar-side right">{props.actions}</div>
        </div>
        {props.accessory ? <div class="navbar-accessory">{props.accessory}</div> : null}
      </header>
      <div class="scroll" ref={scroller} {...touch}>
        {props.onRefresh ? (
          <div class={`pull${refreshing ? ' spinning' : ''}${pull === 0 ? ' idle' : ''}`} style={{ height: `${pull}px` }}>
            <Spinner turn={refreshing ? 1 : progress(pull)} />
          </div>
        ) : null}
        <div class={`scroll-body${pull === 0 && !refreshing ? ' settle' : ''}`} style={shift}>
          {props.large ? (
            <div class="large-title">
              <h1>{props.title}</h1>
              {props.subtitle ? <p class="subtitle">{props.subtitle}</p> : null}
            </div>
          ) : null}
          <div ref={sentinel} class="title-sentinel" />
          {props.children}
        </div>
      </div>
    </section>
  );
}

export function BackButton({ label, to }: { label: string; to: Route }) {
  return (
    <a
      class="back"
      href={href(to)}
      onClick={(e) => {
        e.preventDefault();
        back(to);
      }}
    >
      <ChevronLeft size={22} />
      <span>{label}</span>
    </a>
  );
}

export function IconButton(props: {
  label: string;
  onClick?: () => void;
  href?: string;
  children: ComponentChildren;
  tone?: 'plain' | 'filled';
}) {
  const cls = `icon-button${props.tone === 'filled' ? ' filled' : ''}`;
  if (props.href)
    return (
      <a class={cls} href={props.href} aria-label={props.label} title={props.label}>
        {props.children}
      </a>
    );
  return (
    <button class={cls} type="button" onClick={props.onClick} aria-label={props.label} title={props.label}>
      {props.children}
    </button>
  );
}

/** An inset grouped list, with an optional heading and footnote. */
export function Section(props: { title?: ComponentChildren; footer?: ComponentChildren; children: ComponentChildren; class?: string }) {
  return (
    <div class={`section ${props.class ?? ''}`}>
      {props.title ? <h2 class="section-title">{props.title}</h2> : null}
      <div class="group">{props.children}</div>
      {props.footer ? <p class="section-footer">{props.footer}</p> : null}
    </div>
  );
}

export interface RowProps {
  title: ComponentChildren;
  subtitle?: ComponentChildren;
  detail?: ComponentChildren;
  /** Something on the left: a class dot, an icon. */
  lead?: ComponentChildren;
  href?: string;
  onClick?: () => void;
  chevron?: boolean;
  selected?: boolean;
  /** Lines of subtitle to show before it's cut. */
  lines?: number;
  class?: string;
  external?: boolean;
}

export function Row(props: RowProps) {
  const inner = (
    <>
      {props.lead ? <span class="row-lead">{props.lead}</span> : null}
      <span class="row-text">
        <span class="row-title">{props.title}</span>
        {props.subtitle ? (
          <span class="row-subtitle" style={props.lines ? { WebkitLineClamp: props.lines } : undefined}>
            {props.subtitle}
          </span>
        ) : null}
      </span>
      {props.detail !== undefined ? <span class="row-detail">{props.detail}</span> : null}
      {props.chevron ?? (props.href && !props.external) ? <ChevronRight size={16} class="row-chevron" /> : null}
    </>
  );
  const cls = `row${props.selected ? ' selected' : ''}${props.href || props.onClick ? ' tappable' : ''} ${props.class ?? ''}`;
  if (props.href)
    return (
      <a
        class={cls}
        href={props.href}
        aria-current={props.selected ? 'page' : undefined}
        target={props.external ? '_blank' : undefined}
        rel={props.external ? 'noopener' : undefined}
      >
        {inner}
      </a>
    );
  if (props.onClick)
    return (
      <button class={cls} type="button" onClick={props.onClick}>
        {inner}
      </button>
    );
  return <div class={cls}>{inner}</div>;
}

export function Dot({ color, size = 10 }: { color: string; size?: number }) {
  return <span class="dot" style={{ background: color, width: `${size}px`, height: `${size}px` }} />;
}

export function Segmented<T extends string>(props: {
  value: T;
  options: { value: T; label: string }[];
  onChange: (v: T) => void;
  label: string;
}) {
  const index = Math.max(
    0,
    props.options.findIndex((o) => o.value === props.value),
  );
  return (
    <div
      class="segmented"
      role="tablist"
      aria-label={props.label}
      style={{ '--count': props.options.length, '--index': index } as JSX.CSSProperties}
    >
      <span class="segmented-thumb" aria-hidden="true" />
      {props.options.map((o) => (
        <button
          type="button"
          role="tab"
          aria-selected={o.value === props.value}
          class={o.value === props.value ? 'on' : ''}
          onClick={() => props.onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

export function Spinner({ turn = 1, size = 22 }: { turn?: number; size?: number }) {
  const spokes = 8;
  return (
    <span class="spinner" style={{ width: `${size}px`, height: `${size}px` }} role="progressbar" aria-label="Loading">
      {Array.from({ length: spokes }, (_, i) => (
        <i style={{ transform: `rotate(${i * 45}deg)`, opacity: i / spokes < turn ? 0.25 + (0.75 * i) / spokes : 0 }} />
      ))}
    </span>
  );
}

/** A screen or section with nothing to show yet: an icon, a line in bold, and a sentence. */
export function Empty(props: { icon?: ComponentChildren; title: string; children?: ComponentChildren; action?: ComponentChildren }) {
  return (
    <div class="empty">
      {props.icon ? <div class="empty-icon">{props.icon}</div> : null}
      <h2>{props.title}</h2>
      {props.children ? <p>{props.children}</p> : null}
      {props.action ? <div class="empty-action">{props.action}</div> : null}
    </div>
  );
}

/** Grey bars where rows are about to be, so the screen doesn't jump when they come. */
export function SkeletonRows({ count = 5, withSubtitle = true }: { count?: number; withSubtitle?: boolean }) {
  return (
    <div class="section">
      <div class="group" aria-busy="true" aria-label="Loading">
        {Array.from({ length: count }, (_, i) => (
          <div class="row skeleton">
            <span class="row-text">
              <span class="bar" style={{ width: `${55 + ((i * 17) % 35)}%` }} />
              {withSubtitle ? <span class="bar thin" style={{ width: `${35 + ((i * 23) % 40)}%` }} /> : null}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

/** A problem said plainly, with a way to try again. */
export function Problem({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div class="problem" role="alert">
      <p>{message}</p>
      {onRetry ? (
        <button type="button" class="button tinted small" onClick={onRetry}>
          Try again
        </button>
      ) : null}
    </div>
  );
}

export function Button(props: {
  children: ComponentChildren;
  onClick?: () => void;
  href?: string;
  kind?: 'filled' | 'tinted' | 'plain' | 'danger';
  disabled?: boolean;
  type?: 'button' | 'submit';
  wide?: boolean;
  small?: boolean;
  external?: boolean;
}) {
  const cls = `button ${props.kind ?? 'filled'}${props.wide ? ' wide' : ''}${props.small ? ' small' : ''}`;
  if (props.href)
    return (
      <a class={cls} href={props.href} target={props.external ? '_blank' : undefined} rel={props.external ? 'noopener' : undefined}>
        {props.children}
      </a>
    );
  return (
    <button class={cls} type={props.type ?? 'button'} onClick={props.onClick} disabled={props.disabled}>
      {props.children}
    </button>
  );
}
