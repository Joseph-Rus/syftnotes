import React from 'react';
import {interpolate, useCurrentFrame} from 'remotion';
import {colors, durations, fonts, text} from '../config';
import {outro, rise, spr} from '../anim';
import {ClassDot, Feature, Glass, TrafficLights, useVertical} from '../components/Layout';
import {Sidebar} from '../components/Sidebar';

const W = 1080;
const H = 680;
const VW = 830; // vertical: the Due list and the assignment, without the sidebar
const VH = 720;
const PICK = 44; // "Lab 3" is chosen and opens

type Item = {title: string; sub: string; color: string; right: string; accent?: boolean};
const GROUPS: [string, Item[]][] = [
  ['Overdue', [{title: 'Reading response', sub: 'HIST 210 · Was due Mon', color: colors.hist, right: 'Missing', accent: true}]],
  [
    'This week',
    [
      {title: 'Quiz 3 practice', sub: 'CALC II · Tomorrow, 9:00 AM', color: colors.calc, right: 'To do'},
      {title: 'Lab 3: recursion traces', sub: 'CS 101 · Tue 30 Sep, 11:59 PM', color: colors.cs, right: 'To do'},
    ],
  ],
  ['Handed in', [{title: 'Problem set 4', sub: 'CS 101 · Graded Mon', color: colors.cs, right: '18/20'}]],
];

const RUBRIC: [string, number][] = [
  ['Correct traces', 10],
  ['Stack diagram at the deepest point', 6],
  ['Return values labelled', 4],
];

const DueIcon = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke={colors.lagoonBright} strokeWidth="2" strokeLinecap="round">
    <rect x="3.5" y="5" width="17" height="15" rx="3" />
    <path d="M8 3v4M16 3v4M3.5 10h17" />
  </svg>
);

export const CanvasDue: React.FC = () => {
  const frame = useCurrentFrame();
  const vertical = useVertical();
  const w = vertical ? VW : W;
  const h = vertical ? VH : H;
  const open = spr(frame, PICK + 4);
  let n = 0;
  return (
    <Feature headline={text.canvas} w={w} h={h} opacity={outro(frame, durations.canvas)}>
      <div style={rise(spr(frame, 2), 60)}>
        <Glass style={{width: w, height: h, display: 'flex'}}>
          {vertical ? null : <Sidebar
            footer="Canvas synced 10:24"
            top={
              <div style={{display: 'flex', alignItems: 'center', gap: 12, padding: '11px 12px', marginBottom: 12, borderRadius: 12, background: 'rgba(255,255,255,0.08)', fontSize: 19, fontWeight: 600}}>
                <DueIcon />
                <div style={{flex: 1}}>Due</div>
                <div style={{color: colors.nightInk2, fontWeight: 500}}>3</div>
              </div>
            }
            rows={[
              {name: 'CS 101', color: colors.cs, count: 12},
              {name: 'BIO 110', color: colors.bio, count: 9},
              {name: 'CALC II', color: colors.calc, count: 11},
              {name: 'HLTH 120', color: colors.health, count: 8},
            ]}
          />}
          <div style={{width: 330, padding: '0 16px', borderRight: `1.5px solid ${colors.glassEdge}`, fontFamily: fonts.ui}}>
            <TrafficLights />
            <div style={{padding: '0 10px'}}>
              <div style={{fontFamily: fonts.display, fontSize: 30, fontWeight: 700}}>Due</div>
              <div style={{fontSize: 15, color: colors.nightInk3, marginTop: 2}}>3 to hand in · synced 10:24</div>
            </div>
            {GROUPS.map(([group, items]) => (
              <div key={group}>
                <div style={{fontSize: 15, fontWeight: 600, color: colors.nightInk3, padding: '18px 10px 6px'}}>{group}</div>
                {items.map((it) => {
                  const i = n++;
                  const chosen = it.title.startsWith('Lab 3') && frame >= PICK;
                  return (
                    <div
                      key={it.title}
                      style={{
                        ...rise(spr(frame, 8 + i * 6), 24),
                        padding: '12px 12px',
                        borderRadius: 14,
                        background: chosen ? colors.lagoon : 'transparent',
                        transform: `${rise(spr(frame, 8 + i * 6), 24).transform} scale(${chosen ? 1 + Math.max(0, 1 - (frame - PICK) / 8) * 0.03 : 1})`,
                      }}
                    >
                      <div style={{display: 'flex', fontSize: 18, fontWeight: 600}}>
                        <div style={{flex: 1}}>{it.title}</div>
                        <div style={{fontSize: 15, fontWeight: it.accent ? 700 : 500, color: chosen ? '#fff' : it.accent ? colors.lagoonBright : colors.nightInk2}}>{it.right}</div>
                      </div>
                      <div style={{display: 'flex', alignItems: 'center', gap: 8, marginTop: 5, fontSize: 14.5, color: chosen ? 'rgba(255,255,255,0.85)' : colors.nightInk2}}>
                        <ClassDot color={chosen ? '#fff' : it.color} size={8} />
                        {it.sub}
                      </div>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
          <div style={{flex: 1, padding: '66px 36px 0', fontFamily: fonts.ui, ...rise(open, 30)}}>
            <div style={{display: 'flex', alignItems: 'center', gap: 10, fontSize: 16, color: colors.nightInk2}}>
              <ClassDot color={colors.cs} size={9} /> CS 101 · Assignment
            </div>
            <div style={{fontFamily: fonts.display, fontSize: 34, fontWeight: 700, marginTop: 8, letterSpacing: '-0.02em'}}>Lab 3: recursion traces</div>
            <div style={{display: 'flex', marginTop: 20, borderRadius: 16, background: 'rgba(255,255,255,0.06)', overflow: 'hidden'}}>
              {[
                ['Due', 'Tue 30 Sep'],
                ['Points', '20'],
                ['Status', 'To do'],
              ].map(([k, v], i) => (
                <div key={k} style={{flex: 1, padding: '14px 16px', whiteSpace: 'nowrap', borderLeft: i ? `1.5px solid ${colors.glassEdge}` : 'none'}}>
                  <div style={{fontSize: 14, color: colors.nightInk3}}>{k}</div>
                  <div style={{fontSize: 19, fontWeight: 600, marginTop: 4}}>{v}</div>
                </div>
              ))}
            </div>
            <div style={{fontSize: 21, fontWeight: 700, marginTop: 26}}>Rubric</div>
            {RUBRIC.map(([name, pts], i) => {
              const p = spr(frame, PICK + 20 + i * 8);
              const shown = Math.round(interpolate(p, [0, 1], [0, pts], {extrapolateRight: 'clamp'}));
              return (
                <div key={name} style={{...rise(p, 16), display: 'flex', padding: '13px 0', borderBottom: `1.5px solid ${colors.glassEdge}`, fontSize: 17.5}}>
                  <div style={{flex: 1, fontWeight: 500}}>{name}</div>
                  <div style={{color: colors.nightInk2, fontVariantNumeric: 'tabular-nums'}}>{shown} pts</div>
                </div>
              );
            })}
            <div style={{...rise(spr(frame, PICK + 52), 16), marginTop: 22, padding: '16px 20px', borderRadius: 16, background: colors.lagoonSoft}}>
              <div style={{fontSize: 17, fontWeight: 600}}>
                Nothing handed in yet <span style={{fontWeight: 400, color: colors.nightInk2, marginLeft: 8}}>Due in 5 days</span>
              </div>
              <div style={{fontSize: 16, color: colors.lagoonBright, marginTop: 6, fontWeight: 600}}>Hand it in on Canvas ↗</div>
            </div>
          </div>
        </Glass>
      </div>
    </Feature>
  );
};
