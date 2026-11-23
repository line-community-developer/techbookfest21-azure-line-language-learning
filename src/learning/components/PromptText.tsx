import { Fragment } from 'react';
import { cx } from './ui';

const BLANK = /_{2,}/g;

// Renders an exercise prompt with every `___` blank visually highlighted (optionally filled in).
export function PromptText({ prompt, fill, fillTone = 'green', className }: { prompt: string; fill?: string; fillTone?: 'green' | 'red'; className?: string }) {
  const parts = prompt.split(BLANK);
  return (
    <span className={className}>
      {parts.map((part, index) => (
        <Fragment key={index}>
          {part}
          {index < parts.length - 1 &&
            (fill ? (
              <span
                className={cx(
                  'mx-0.5 rounded-md px-1.5 py-0.5 font-bold underline decoration-2 underline-offset-4',
                  fillTone === 'green'
                    ? 'bg-green-100 text-green-800 decoration-line dark:bg-green-950 dark:text-green-300'
                    : 'bg-red-100 text-red-800 decoration-red-500 dark:bg-red-950 dark:text-red-300',
                )}
              >
                {fill}
              </span>
            ) : (
              <span
                aria-label="空欄"
                className="mx-0.5 inline-block min-w-14 rounded-md border-b-2 border-line bg-green-50 px-2 text-center align-baseline text-line dark:bg-green-950/50"
              >
                ？
              </span>
            ))}
        </Fragment>
      ))}
    </span>
  );
}
