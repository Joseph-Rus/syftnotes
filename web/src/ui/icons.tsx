import type { JSX } from 'preact';

// Line icons drawn to sit beside SF Symbols: a 24-unit box, round ends, the stroke in the current colour.

type Props = { size?: number; class?: string; filled?: boolean; title?: string };

function Svg({ size = 24, class: cls, title, children }: Props & { children: JSX.Element | JSX.Element[] }) {
  return (
    <svg
      class={cls ? `icon ${cls}` : 'icon'}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.8"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden={title ? undefined : 'true'}
      role={title ? 'img' : undefined}
    >
      {title ? <title>{title}</title> : <></>}
      {children}
    </svg>
  );
}

export const Books = (p: Props) => (
  <Svg {...p}>
    <path d="M4 4.5h3.5v15H4z" fill={p.filled ? 'currentColor' : 'none'} />
    <path d="M9.5 4.5H13v15H9.5z" fill={p.filled ? 'currentColor' : 'none'} />
    <path d="m15.2 5.6 3.3-1 3.3 14.4-3.3 1z" fill={p.filled ? 'currentColor' : 'none'} />
  </Svg>
);

export const Search = (p: Props) => (
  <Svg {...p}>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="m15.5 15.5 5 5" />
  </Svg>
);

export const Sparkle = (p: Props) => (
  <Svg {...p}>
    <path
      d="M12 3.5c.6 3.9 2.6 5.9 6.5 6.5-3.9.6-5.9 2.6-6.5 6.5-.6-3.9-2.6-5.9-6.5-6.5 3.9-.6 5.9-2.6 6.5-6.5Z"
      fill={p.filled ? 'currentColor' : 'none'}
    />
    <path d="M18.5 15.5c.2 1.4.9 2.1 2.3 2.3-1.4.2-2.1.9-2.3 2.3-.2-1.4-.9-2.1-2.3-2.3 1.4-.2 2.1-.9 2.3-2.3Z" />
  </Svg>
);

export const Calendar = (p: Props) => (
  <Svg {...p}>
    <rect x="3.5" y="5" width="17" height="15.5" rx="3" fill={p.filled ? 'currentColor' : 'none'} />
    <path d="M3.5 9.5h17" stroke={p.filled ? 'var(--tab-knock, #fff)' : 'currentColor'} />
    <path d="M8 3v4M16 3v4" />
  </Svg>
);

export const PlusCircle = (p: Props) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" fill={p.filled ? 'currentColor' : 'none'} />
    <path d="M12 8v8M8 12h8" stroke={p.filled ? 'var(--tab-knock, #fff)' : 'currentColor'} />
  </Svg>
);

export const Gear = (p: Props) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="3" />
    <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
  </Svg>
);

export const ChevronLeft = (p: Props) => (
  <Svg {...p}>
    <path d="m15 4.5-7.5 7.5 7.5 7.5" stroke-width="2.4" />
  </Svg>
);

export const ChevronRight = (p: Props) => (
  <Svg {...p}>
    <path d="m9.5 6 6 6-6 6" stroke-width="2.2" />
  </Svg>
);

export const Doc = (p: Props) => (
  <Svg {...p}>
    <path d="M14 3.5H7.5a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2V8Z" />
    <path d="M14 3.5V8h4.5M9 13h6M9 16.5h4" />
  </Svg>
);

export const Paperclip = (p: Props) => (
  <Svg {...p}>
    <path d="m20 11.5-7.8 7.8a5 5 0 0 1-7.1-7.1l7.8-7.8a3.4 3.4 0 0 1 4.8 4.8l-7.6 7.6a1.7 1.7 0 0 1-2.4-2.4l7-7" />
  </Svg>
);

export const Camera = (p: Props) => (
  <Svg {...p}>
    <path d="M4 8.5a2 2 0 0 1 2-2h2l1.5-2h5l1.5 2h2a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2Z" />
    <circle cx="12" cy="13" r="3.8" />
  </Svg>
);

export const Photo = (p: Props) => (
  <Svg {...p}>
    <rect x="3.5" y="4.5" width="17" height="15" rx="3" />
    <circle cx="9" cy="9.5" r="1.6" />
    <path d="m4 17 4.8-4.6 3.4 3.2 2.8-2.6 5 4.6" />
  </Svg>
);

export const Folder = (p: Props) => (
  <Svg {...p}>
    <path d="M3.5 7a2 2 0 0 1 2-2h4l2 2.2h7a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2Z" />
  </Svg>
);

export const Tray = (p: Props) => (
  <Svg {...p}>
    <path d="M3.5 13.5 6 5.5h12l2.5 8V18a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 18Z" />
    <path d="M3.5 13.5H8a4 4 0 0 0 8 0h4.5" />
  </Svg>
);

export const Close = (p: Props) => (
  <Svg {...p}>
    <path d="M6.5 6.5l11 11M17.5 6.5l-11 11" stroke-width="2.2" />
  </Svg>
);

export const Check = (p: Props) => (
  <Svg {...p}>
    <path d="m5 12.5 4.5 4.5L19 7.5" stroke-width="2.4" />
  </Svg>
);

export const ArrowUp = (p: Props) => (
  <Svg {...p}>
    <path d="M12 19V5.5M6 11l6-6 6 6" stroke-width="2.4" />
  </Svg>
);

/** iOS's Share: a box with an arrow out of its top. */
export const Share = (p: Props) => (
  <Svg {...p}>
    <path d="M8 9.5H6.5a2 2 0 0 0-2 2V19a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-7.5a2 2 0 0 0-2-2H16" />
    <path d="M12 14V3M8.5 6.5 12 3l3.5 3.5" />
  </Svg>
);

/** iOS's Add to Home Screen: a plus in a rounded square. */
export const AddSquare = (p: Props) => (
  <Svg {...p}>
    <rect x="3.5" y="3.5" width="17" height="17" rx="4" />
    <path d="M12 8v8M8 12h8" />
  </Svg>
);

export const Offline = (p: Props) => (
  <Svg {...p}>
    <path d="M2.5 8.8a14 14 0 0 1 5-2.7M21.5 8.8a14 14 0 0 0-8-3.3M5.5 12.2a9.5 9.5 0 0 1 3.4-2M18.5 12.2a9.5 9.5 0 0 0-2.7-1.8M9 15.5a4.5 4.5 0 0 1 5.3-.5" />
    <circle cx="12" cy="19" r="1" fill="currentColor" />
    <path d="m3.5 3.5 17 17" />
  </Svg>
);

export const Clock = (p: Props) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="8.5" />
    <path d="M12 7.5V12l3 2" />
  </Svg>
);

export const Pin = (p: Props) => (
  <Svg {...p}>
    <path d="M12 21s-6.5-5.6-6.5-11a6.5 6.5 0 0 1 13 0c0 5.4-6.5 11-6.5 11Z" />
    <circle cx="12" cy="10" r="2.3" />
  </Svg>
);

export const Trash = (p: Props) => (
  <Svg {...p}>
    <path d="M4.5 6.5h15M9.5 6.5V4.5h5v2M6.5 6.5l1 13a1.5 1.5 0 0 0 1.5 1.4h6a1.5 1.5 0 0 0 1.5-1.4l1-13" />
  </Svg>
);

export const Refresh = (p: Props) => (
  <Svg {...p}>
    <path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4h-4" />
  </Svg>
);

export const External = (p: Props) => (
  <Svg {...p}>
    <path d="M13.5 4.5h6v6M19.5 4.5l-8 8M17.5 14v4a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h4" />
  </Svg>
);

export const Phone = (p: Props) => (
  <Svg {...p}>
    <rect x="6.5" y="2.5" width="11" height="19" rx="2.5" />
    <path d="M10.5 18.5h3" />
  </Svg>
);

export const Chat = (p: Props) => (
  <Svg {...p}>
    <path d="M20.5 11.5c0 4.1-3.8 7.5-8.5 7.5-1.1 0-2.1-.2-3-.5L4 20l1.3-3.6a7 7 0 0 1-1.8-4.9C3.5 7.4 7.3 4 12 4s8.5 3.4 8.5 7.5Z" />
  </Svg>
);
