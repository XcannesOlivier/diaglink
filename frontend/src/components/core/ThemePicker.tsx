import { useMemo } from 'react';
import { useThemeContext, type ThemeType } from '../../contexts/ThemeContext';
import { Dropdown, Option, Label } from '@fluentui/react-components';

interface IDropdownItem {
  key: ThemeType;
  value: ThemeType;
  text: string;
}

export function ThemePicker() {
  const { savedTheme, setTheme } = useThemeContext();

  const options: IDropdownItem[] = useMemo(
    () => [
      {
        key: 'Light',
        value: 'Light',
        text: 'Clair',
      },
      {
        key: 'Dark',
        value: 'Dark',
        text: 'Sombre',
      },
    ],
    []
  );

  const selectedThemeText = useMemo(
    () =>
      options.find((opt) => opt.key === (savedTheme ?? 'Light'))?.text ??
      'Clair',
    [savedTheme, options]
  );

  const selectedOptions = useMemo(
    () => (savedTheme ? [savedTheme] : []),
    [savedTheme]
  );

  return (
    <>
      <Label htmlFor="ThemePickerDropdown">Thème</Label>
      <Dropdown
        id="ThemePickerDropdown"
        onOptionSelect={(_, { optionValue }) => {
          if (optionValue !== undefined) {
            setTheme(optionValue as ThemeType);
          }
        }}
        selectedOptions={selectedOptions}
        value={selectedThemeText}
      >
        {options.map((option) => (
          <Option key={option.key} value={option.value}>
            {option.text}
          </Option>
        ))}
      </Dropdown>
    </>
  );
}
